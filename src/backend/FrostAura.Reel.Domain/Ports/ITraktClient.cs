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

    /// <summary>All watched movies with extended=full metadata (not paginated by Trakt).</summary>
    Task<IReadOnlyList<TraktWatchedMovie>> GetWatchedMoviesAsync(string accessToken, RatePriority priority, CancellationToken ct = default);

    /// <summary>All watched shows incl. per-episode plays (?extended=full).</summary>
    Task<IReadOnlyList<TraktWatchedShow>> GetWatchedShowsAsync(string accessToken, RatePriority priority, CancellationToken ct = default);

    /// <summary>Every rating across movies/shows/seasons/episodes with extended=full subjects.</summary>
    Task<IReadOnlyList<TraktRatingItem>> GetRatingsAsync(string accessToken, RatePriority priority, CancellationToken ct = default);

    /// <summary>Watched progress for one show — aired/completed counts + the next episode.</summary>
    Task<TraktShowProgress?> GetShowProgressAsync(string accessToken, long showTraktId, RatePriority priority, CancellationToken ct = default);

    /// <summary>The 1-call delta gate: raw /sync/last_activities JSON for snapshot diffing.</summary>
    Task<string> GetLastActivitiesRawAsync(string accessToken, RatePriority priority, CancellationToken ct = default);
}
