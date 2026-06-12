namespace FrostAura.Reel.Domain.Ports.Trakt;

/// <summary>External-id bundle present on every Trakt media object.</summary>
public record TraktIds(long Trakt, string? Slug, string? Imdb, long? Tmdb);

/// <summary>Movie object (extended=full slice Reel persists).</summary>
public record TraktMovie(
    string Title,
    int? Year,
    TraktIds Ids,
    string? Tagline,
    string? Overview,
    DateTime? Released,
    int? Runtime,
    string? Country,
    string? Status,
    decimal? Rating,
    int Votes,
    string? Trailer,
    string? Language,
    string[]? Genres,
    string[]? Subgenres,
    string? Certification);

/// <summary>Show object (extended=full slice Reel persists).</summary>
public record TraktShow(
    string Title,
    int? Year,
    TraktIds Ids,
    string? Tagline,
    string? Overview,
    DateTime? FirstAired,
    int? Runtime,
    string? Country,
    string? Status,
    decimal? Rating,
    int Votes,
    string? Trailer,
    string? Language,
    string[]? Genres,
    string[]? Subgenres,
    string? Certification,
    string? Network,
    int? AiredEpisodes);

public record TraktWatchedMovie(int Plays, DateTime? LastWatchedAt, DateTime? LastUpdatedAt, TraktMovie Movie);

public record TraktWatchedEpisode(int Number, int Plays, DateTime? LastWatchedAt);

public record TraktWatchedSeason(int Number, TraktWatchedEpisode[] Episodes);

public record TraktWatchedShow(
    int Plays,
    DateTime? LastWatchedAt,
    DateTime? LastUpdatedAt,
    DateTime? ResetAt,
    TraktShow Show,
    TraktWatchedSeason[]? Seasons);

/// <summary>One /users/{slug}/ratings item — exactly one of movie/show is set; season/episode carry numbers.</summary>
public record TraktRatingItem(
    DateTime RatedAt,
    short Rating,
    string Type,
    TraktMovie? Movie,
    TraktShow? Show,
    TraktSeasonRef? Season,
    TraktEpisodeRef? Episode);

public record TraktSeasonRef(int Number);

public record TraktEpisodeRef(int? Season, int Number, string? Title);

/// <summary>GET shows/{id}/progress/watched essentials.</summary>
public record TraktShowProgress(int Aired, int Completed, TraktNextEpisode? NextEpisode);

public record TraktNextEpisode(int Season, int Number, string? Title);
