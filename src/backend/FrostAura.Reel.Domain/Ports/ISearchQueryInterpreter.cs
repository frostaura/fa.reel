using FrostAura.Reel.Domain.Catalog;

namespace FrostAura.Reel.Domain.Ports;

/// <summary>
/// A natural-language search query resolved into a structured TMDB discovery spec — the bridge
/// between "shark horrors" and the keyword/genre/text calls that pull the real titles. Genres are
/// slugs aligned with the ingest vocabulary (<c>TmdbGenres.SlugToIds</c>); keywords are free
/// phrases resolved to TMDB keyword ids; FreeText is the residual for literal title search.
/// </summary>
public sealed record SearchIntent(
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<MediaType> MediaTypes,
    string FreeText,
    int? MinYear);

public interface ISearchQueryInterpreter
{
    /// <summary>True when a live model OR the deterministic stub is available (the stub always answers).</summary>
    bool IsAvailable { get; }

    string ModelId { get; }

    /// <summary>Parse a free-text query into a discovery spec. Never throws — degrades to a keyword-only intent.</summary>
    Task<SearchIntent> InterpretAsync(string query, CancellationToken ct = default);
}
