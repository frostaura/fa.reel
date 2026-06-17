using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Pgvector;

namespace FrostAura.Reel.Api.Endpoints;

/// <summary>
/// Positive preference tags — "things I'm into" (e.g. "magical serial killer mysteries"). The
/// inverse of exclusion filters: each is embedded on create so it can BOOST matching titles in
/// the feed + Ask Reel. CRUD only; the boost lives in the ranking paths.
/// </summary>
public static class PreferenceTagEndpoints
{
    public static void MapPreferenceTagEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/preferences/tags").RequireAccount();

        group.MapGet("/", async (IReelDbContext db, IAccountContext ctx, CancellationToken ct) =>
        {
            var accountId = ctx.AccountId!.Value;
            var tags = await db.UserPreferenceTags
                .Where(t => t.AccountId == accountId)
                .OrderByDescending(t => t.CreatedAt)
                .Select(t => new { id = t.Id, text = t.Text })
                .ToListAsync(ct);
            return Results.Ok(tags);
        });

        group.MapPost("/", async (
            CreateTagRequest request, IReelDbContext db, IAccountContext ctx, IEmbeddingProvider embeddings, CancellationToken ct) =>
        {
            var accountId = ctx.AccountId!.Value;
            var text = (request.Text ?? string.Empty).Trim();
            if (text.Length is < 2 or > 120)
            {
                return Results.BadRequest(new { error = "Tag must be 2–120 characters." });
            }

            var existing = await db.UserPreferenceTags
                .FirstOrDefaultAsync(t => t.AccountId == accountId && t.Text == text, ct);
            if (existing is not null)
            {
                return Results.Ok(new { id = existing.Id, text = existing.Text });
            }

            var tag = new UserPreferenceTag
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                Text = text,
                CreatedAt = DateTime.UtcNow,
            };

            if (embeddings.IsAvailable)
            {
                var vectors = await embeddings.EmbedAsync([text], ct);
                tag.Embedding = new Vector(vectors[0]);
                tag.EmbeddingModel = Application.Ml.EmbeddingText.Model;
            }

            db.UserPreferenceTags.Add(tag);
            await db.SaveChangesAsync(ct);
            return Results.Ok(new { id = tag.Id, text = tag.Text });
        });

        group.MapDelete("/{id:guid}", async (Guid id, IReelDbContext db, IAccountContext ctx, CancellationToken ct) =>
        {
            var accountId = ctx.AccountId!.Value;
            var tag = await db.UserPreferenceTags.FirstOrDefaultAsync(t => t.Id == id && t.AccountId == accountId, ct);
            if (tag is null)
            {
                return Results.NotFound();
            }

            db.UserPreferenceTags.Remove(tag);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }

    public record CreateTagRequest(string Text);
}
