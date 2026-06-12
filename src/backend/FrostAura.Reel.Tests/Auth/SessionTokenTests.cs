using FrostAura.Reel.Application.Auth;
using FrostAura.Reel.Domain.Tenancy;
using Microsoft.Extensions.Configuration;

namespace FrostAura.Reel.Tests.Auth;

/// <summary>JWT mint/validate roundtrip — pure paths of SessionService (no database touched).</summary>
public class SessionTokenTests
{
    private static SessionService CreateService(Dictionary<string, string?>? overrides = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["JWT_SECRET"] = "unit-test-secret-at-least-32-chars!!",
            ["JWT_ISSUER"] = "frostaura-reel",
            ["JWT_AUDIENCE"] = "frostaura-reel-app",
            ["JWT_ACCESS_TOKEN_EXPIRY_MINUTES"] = "15",
        };
        foreach (var (key, value) in overrides ?? [])
        {
            settings[key] = value;
        }

        // Token mint/validate never touches persistence; passing null keeps the test honest
        // about that (it would throw loudly if that ever changed).
        return new SessionService(null!, new ConfigurationBuilder().AddInMemoryCollection(settings).Build());
    }

    private static Account TestAccount() => new()
    {
        Id = Guid.NewGuid(),
        TraktUserSlug = "deanmar09",
        Tier = AccountTier.Founder,
    };

    [Fact]
    public async Task Minted_access_token_validates_to_the_account()
    {
        var service = CreateService();
        var account = TestAccount();

        var token = service.MintAccessToken(account, DateTime.UtcNow);
        var resolved = await service.ValidateAccessTokenAsync(token);

        Assert.Equal(account.Id, resolved);
    }

    [Fact]
    public async Task Expired_token_fails_validation()
    {
        var service = CreateService();
        var token = service.MintAccessToken(TestAccount(), DateTime.UtcNow.AddHours(-2));
        Assert.Null(await service.ValidateAccessTokenAsync(token));
    }

    [Fact]
    public async Task Token_signed_with_a_different_secret_fails()
    {
        var minter = CreateService(new() { ["JWT_SECRET"] = "a-completely-different-32char-secret" });
        var validator = CreateService();

        var token = minter.MintAccessToken(TestAccount(), DateTime.UtcNow);
        Assert.Null(await validator.ValidateAccessTokenAsync(token));
    }

    [Fact]
    public async Task Garbage_token_fails_without_throwing()
    {
        var service = CreateService();
        Assert.Null(await service.ValidateAccessTokenAsync("not-a-jwt"));
    }
}
