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

    /// <summary>Resolves a TMDB id to its Trakt id (discovery titles lack one until write-back).</summary>
    Task<long?> ResolveTraktIdByTmdbAsync(string accessToken, long tmdbId, bool movie, RatePriority priority, CancellationToken ct = default);

    /// <summary>Posts a batched /sync/* payload (ratings, history, watchlist — add or remove endpoints).</summary>
    Task PostSyncBatchAsync(string accessToken, string endpoint, object payload, RatePriority priority, CancellationToken ct = default);

    /// <summary>Finds or creates the named personal list and returns its Trakt id.</summary>
    Task<long> EnsureListAsync(string accessToken, string name, string description, RatePriority priority, CancellationToken ct = default);

    /// <summary>Adds or removes items on a personal list.</summary>
    Task PostListItemsAsync(string accessToken, long listId, object payload, bool remove, RatePriority priority, CancellationToken ct = default);
}
