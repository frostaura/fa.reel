namespace FrostAura.Reel.Domain.Ports.Trakt;

/// <summary>Trakt /oauth/token response.</summary>
public record TraktTokenResponse(
    string AccessToken,
    string RefreshToken,
    string Scope,
    long CreatedAt,
    long ExpiresIn);

/// <summary>The slice of Trakt /users/settings Reel cares about.</summary>
public record TraktUserSettings(
    string Slug,
    string Username,
    string? Name,
    bool Private,
    string? AvatarUrl);
