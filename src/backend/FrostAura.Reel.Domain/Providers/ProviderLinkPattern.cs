namespace FrostAura.Reel.Domain.Providers;

/// <summary>
/// One URL template in the where-to-watch link ladder. Tokens: {titleUrlEncoded}, {year},
/// {imdbId}, {tmdbId}, {region}. Health-checked daily against a canary title; unhealthy
/// patterns silently fall through to the TMDB watch page.
/// </summary>
public class ProviderLinkPattern
{
    public Guid Id { get; set; }

    public Guid ProviderId { get; set; }

    /// <summary>ISO region the pattern applies to; null = all regions.</summary>
    public string? Region { get; set; }

    public string UrlTemplate { get; set; } = string.Empty;

    public LinkKind Kind { get; set; } = LinkKind.DirectSearch;

    /// <summary>Lower wins when several patterns match (region-specific beats global upstream).</summary>
    public int Priority { get; set; }

    public bool IsHealthy { get; set; } = true;

    public int ConsecutiveFailures { get; set; }

    public DateTime? LastCheckedAt { get; set; }

    public int? LastHttpStatus { get; set; }

    /// <summary>Known-carried title used for health probes.</summary>
    public Guid? CanaryTitleId { get; set; }

    public string? Notes { get; set; }
}
