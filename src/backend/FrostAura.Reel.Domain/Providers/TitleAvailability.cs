namespace FrostAura.Reel.Domain.Providers;

/// <summary>
/// Region-scoped watch-provider snapshot for one title — shared cache of TMDB
/// watch/providers (JustWatch-powered), refreshed when older than 24h, on demand.
/// </summary>
public class TitleAvailability
{
    public Guid Id { get; set; }

    public Guid TitleId { get; set; }

    /// <summary>ISO-3166 alpha-2.</summary>
    public string Region { get; set; } = string.Empty;

    public Guid ProviderId { get; set; }

    public AvailabilityKind Kind { get; set; }

    public DateTime FetchedAt { get; set; }
}
