namespace FrostAura.Reel.Domain.Catalog;

/// <summary>
/// Movie vs show. Part of every external-id uniqueness key — TMDB ids collide across the
/// movie and TV namespaces, so an id is only meaningful together with its media type.
/// </summary>
public enum MediaType
{
    Movie,
    Show,
}
