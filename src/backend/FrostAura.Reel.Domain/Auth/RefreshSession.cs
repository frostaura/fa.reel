using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Domain.Auth;

/// <summary>
/// One app-session refresh token (reel_rt cookie), stored as a SHA-256 hash and rotated on
/// every refresh; the replaced session records its successor for reuse detection.
/// </summary>
public class RefreshSession : IAccountScoped
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public string TokenHash { get; set; } = string.Empty;

    public DateTime ExpiresAt { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    public Guid? ReplacedBySessionId { get; set; }

    public string? UserAgent { get; set; }
}
