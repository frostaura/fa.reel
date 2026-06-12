namespace FrostAura.Reel.Domain.Sync;

public enum ApiProvider
{
    Trakt,
    Tmdb,
    OpenRouter,
}

/// <summary>
/// Daily call counters per external provider — Fair-Use monitoring from day 1, surfaced on
/// the pipeline metrics view. The M4 exit criterion reads these (&lt;60% of budget at 6 accounts).
/// </summary>
public class ExternalApiUsage
{
    public Guid Id { get; set; }

    public ApiProvider Provider { get; set; }

    public DateTime Day { get; set; }

    public long CallCount { get; set; }

    public long? TokensUsed { get; set; }
}
