using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Domain.Auth;
using FrostAura.Reel.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace FrostAura.Reel.Application.Auth;

public record SessionTokens(string AccessToken, string RefreshToken, DateTime AccessExpiresAt, DateTime RefreshExpiresAt);

/// <summary>
/// App-session lifecycle: short-lived access JWTs + rotated refresh tokens (hashes only at
/// rest). Trakt OAuth is the identity; these tokens are merely the app's session transport
/// (HttpOnly cookies at the API edge).
/// </summary>
public class SessionService(IReelDbContext db, IConfiguration configuration)
{
    private static readonly JsonWebTokenHandler TokenHandler = new();

    private string Issuer => configuration["JWT_ISSUER"] ?? "frostaura-reel";
    private string Audience => configuration["JWT_AUDIENCE"] ?? "frostaura-reel-app";
    private int AccessMinutes => int.TryParse(configuration["JWT_ACCESS_TOKEN_EXPIRY_MINUTES"], out var m) ? m : 15;
    private int RefreshDays => int.TryParse(configuration["JWT_REFRESH_TOKEN_EXPIRY_DAYS"], out var d) ? d : 30;

    private SymmetricSecurityKey SigningKey
    {
        get
        {
            var secret = configuration["JWT_SECRET"] ?? throw new InvalidOperationException("JWT_SECRET is required.");
            return new SymmetricSecurityKey(SHA256.HashData(Encoding.UTF8.GetBytes("reel.jwt.v1:" + secret)));
        }
    }

    public async Task<SessionTokens> IssueAsync(Account account, string? userAgent, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var refreshToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var session = new RefreshSession
        {
            Id = Guid.NewGuid(),
            AccountId = account.Id,
            TokenHash = Hash(refreshToken),
            ExpiresAt = now.AddDays(RefreshDays),
            CreatedAt = now,
            UserAgent = userAgent is { Length: > 0 } ua ? ua[..Math.Min(ua.Length, 256)] : null,
        };
        db.RefreshSessions.Add(session);
        await db.SaveChangesAsync(ct);

        return new SessionTokens(
            MintAccessToken(account, now),
            refreshToken,
            now.AddMinutes(AccessMinutes),
            session.ExpiresAt);
    }

    /// <summary>Rotates the refresh token; the old session records its successor (reuse detection).</summary>
    public async Task<SessionTokens?> RefreshAsync(string refreshToken, string? userAgent, CancellationToken ct = default)
    {
        var hash = Hash(refreshToken);
        var session = await db.RefreshSessions
            .IgnoreQueryFilters() // pre-auth: no account scope exists yet
            .FirstOrDefaultAsync(s => s.TokenHash == hash, ct);

        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= DateTime.UtcNow)
        {
            return null;
        }

        var account = await db.Accounts.FirstOrDefaultAsync(a => a.Id == session.AccountId, ct);
        if (account is null)
        {
            return null;
        }

        session.RevokedAt = DateTime.UtcNow;
        var tokens = await IssueAsync(account, userAgent, ct);
        var successor = await db.RefreshSessions
            .IgnoreQueryFilters()
            .Where(s => s.AccountId == account.Id)
            .OrderByDescending(s => s.CreatedAt)
            .FirstAsync(ct);
        session.ReplacedBySessionId = successor.Id;
        account.LastSeenAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        return tokens;
    }

    public async Task RevokeAsync(string refreshToken, CancellationToken ct = default)
    {
        var hash = Hash(refreshToken);
        var session = await db.RefreshSessions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TokenHash == hash, ct);
        if (session is not null && session.RevokedAt is null)
        {
            session.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }
    }

    public string MintAccessToken(Account account, DateTime now) =>
        TokenHandler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(AccessMinutes),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = account.Id.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString("N"),
                ["tier"] = account.Tier.ToString(),
            },
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256),
        });

    /// <summary>Validates an access JWT and returns the account id, or null when invalid/expired.</summary>
    public async Task<Guid?> ValidateAccessTokenAsync(string accessToken)
    {
        var result = await TokenHandler.ValidateTokenAsync(accessToken, new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            IssuerSigningKey = SigningKey,
            ClockSkew = TimeSpan.FromSeconds(30),
        });

        if (!result.IsValid)
        {
            return null;
        }

        var sub = result.Claims.TryGetValue(JwtRegisteredClaimNames.Sub, out var value) ? value?.ToString()
            : result.Claims.TryGetValue(ClaimTypes.NameIdentifier, out var mapped) ? mapped?.ToString()
            : null;
        return Guid.TryParse(sub, out var accountId) ? accountId : null;
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
