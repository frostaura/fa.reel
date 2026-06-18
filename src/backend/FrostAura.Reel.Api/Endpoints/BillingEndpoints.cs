using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;

namespace FrostAura.Reel.Api.Endpoints;

/// <summary>
/// Stripe billing — deploy-ready scaffolding gated on STRIPE_SECRET_KEY + STRIPE_PRICE_ID. With
/// no keys it degrades to "not configured" (the founder beta runs on hand-granted Founder tier);
/// the moment the keys land it's a working Checkout → webhook → Paid-tier flow, no code change.
/// The webhook is unauthenticated (Stripe calls it) and maps back to the account via
/// client_reference_id; subscription cancellation maps via the stored subscription id.
/// </summary>
public static class BillingEndpoints
{
    public static void MapBillingEndpoints(this IEndpointRouteBuilder app)
    {
        var secured = app.MapGroup("/api/billing").RequireAccount();

        secured.MapGet("/status", async (HttpContext http, IReelDbContext db, IConfiguration cfg, CancellationToken ct) =>
        {
            var account = await http.GetCurrentAccountAsync(db, ct);
            return account is null
                ? Results.Unauthorized()
                : Results.Ok(new { tier = account.Tier.ToString(), configured = IsConfigured(cfg) });
        });

        secured.MapPost("/checkout", async (HttpContext http, IReelDbContext db, IConfiguration cfg, CancellationToken ct) =>
        {
            var account = await http.GetCurrentAccountAsync(db, ct);
            if (account is null)
            {
                return Results.Unauthorized();
            }

            if (!IsConfigured(cfg))
            {
                return Results.Json(new { error = "Billing isn't switched on yet." }, statusCode: StatusCodes.Status501NotImplemented);
            }

            if (account.Tier is AccountTier.Paid or AccountTier.Founder)
            {
                return Results.Json(new { error = "You're already on a paid plan." }, statusCode: StatusCodes.Status409Conflict);
            }

            StripeConfiguration.ApiKey = cfg["STRIPE_SECRET_KEY"];
            var publicUrl = (cfg["APP_PUBLIC_URL"] ?? "http://localhost:8090").TrimEnd('/');
            var options = new SessionCreateOptions
            {
                Mode = "subscription",
                LineItems = [new SessionLineItemOptions { Price = cfg["STRIPE_PRICE_ID"], Quantity = 1 }],
                ClientReferenceId = account.Id.ToString(),
                CustomerEmail = account.Settings.EmailForBilling,
                SuccessUrl = $"{publicUrl}/settings?upgraded=1",
                CancelUrl = $"{publicUrl}/settings",
            };
            var session = await new SessionService().CreateAsync(options, cancellationToken: ct);
            return Results.Ok(new { url = session.Url });
        });

        // Unauthenticated — Stripe calls it. Signature-verified; a no-op when not configured.
        app.MapPost("/api/billing/webhook", async (HttpContext http, IReelDbContext db, IConfiguration cfg, CancellationToken ct) =>
        {
            var secret = cfg["STRIPE_WEBHOOK_SECRET"];
            if (string.IsNullOrWhiteSpace(secret))
            {
                return Results.Ok();
            }

            using var reader = new StreamReader(http.Request.Body);
            var json = await reader.ReadToEndAsync(ct);
            Event stripeEvent;
            try
            {
                stripeEvent = EventUtility.ConstructEvent(json, http.Request.Headers["Stripe-Signature"], secret);
            }
            catch (StripeException)
            {
                return Results.BadRequest();
            }

            switch (stripeEvent.Type)
            {
                case "checkout.session.completed":
                    if (stripeEvent.Data.Object is Session session
                        && Guid.TryParse(session.ClientReferenceId, out var accountId))
                    {
                        var acct = await db.Accounts.FirstOrDefaultAsync(a => a.Id == accountId, ct);
                        if (acct is not null)
                        {
                            acct.Tier = AccountTier.Paid;
                            acct.StripeCustomerId = session.CustomerId;
                            acct.StripeSubscriptionId = session.SubscriptionId;
                            if (session.CustomerEmail is { Length: > 0 } email)
                            {
                                acct.Settings.EmailForBilling = email;
                            }

                            await db.SaveChangesAsync(ct);
                        }
                    }

                    break;

                case "customer.subscription.deleted":
                    if (stripeEvent.Data.Object is Subscription sub)
                    {
                        var acct = await db.Accounts.FirstOrDefaultAsync(a => a.StripeSubscriptionId == sub.Id, ct);
                        if (acct is { Tier: AccountTier.Paid })
                        {
                            acct.Tier = AccountTier.Free;
                            await db.SaveChangesAsync(ct);
                        }
                    }

                    break;
            }

            return Results.Ok();
        });
    }

    private static bool IsConfigured(IConfiguration cfg) =>
        !string.IsNullOrWhiteSpace(cfg["STRIPE_SECRET_KEY"]) && !string.IsNullOrWhiteSpace(cfg["STRIPE_PRICE_ID"]);
}
