using FrostAura.Reel.Domain.Ports.Trakt;

namespace FrostAura.Reel.Domain.Ports;

/// <summary>
/// Trakt API port. OAuth + profile now; ingestion/write-back methods join with the sync
/// subsystem. Every call rides the shared priority rate gate inside the adapter.
/// </summary>
public interface ITraktClient
{
    Task<TraktTokenResponse> ExchangeCodeAsync(string code, CancellationToken ct = default);

    Task<TraktTokenResponse> RefreshTokenAsync(string refreshToken, CancellationToken ct = default);

    Task<TraktUserSettings> GetUserSettingsAsync(string accessToken, CancellationToken ct = default);
}
