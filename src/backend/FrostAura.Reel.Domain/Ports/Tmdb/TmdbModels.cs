namespace FrostAura.Reel.Domain.Ports.Tmdb;

/// <summary>Unified movie/TV hydration payload (the union Reel persists from either endpoint).</summary>
public record TmdbTitleDetails(
    long Id,
    string? PosterPath,
    string? BackdropPath,
    decimal Popularity,
    decimal VoteAverage,
    int VoteCount,
    int? Runtime,
    string? Overview,
    string? Tagline,
    int? NumberOfEpisodes,
    TmdbCastMember[] Cast,
    TmdbCrewMember[] Crew,
    string? TrailerYouTubeKey,
    string[] Keywords);

public record TmdbCastMember(long Id, string Name, string? Character, int Order, string? ProfilePath, string? KnownForDepartment);

public record TmdbCrewMember(long Id, string Name, string? Job, string? Department, string? ProfilePath);
