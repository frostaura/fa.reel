namespace FrostAura.Reel.Domain.Catalog;

/// <summary>
/// Episode of a show. Hydrated lazily — only for shows some account has in progress, because
/// continue-watching needs next-episode names/air dates. Full-catalog episode hydration would
/// be millions of dead rows.
/// </summary>
public class Episode
{
    public Guid Id { get; set; }

    /// <summary>Parent show's Title id.</summary>
    public Guid TitleId { get; set; }

    public int SeasonNumber { get; set; }

    public int EpisodeNumber { get; set; }

    public string? Name { get; set; }

    public DateTime? AiredAt { get; set; }

    public int? RuntimeMinutes { get; set; }
}
