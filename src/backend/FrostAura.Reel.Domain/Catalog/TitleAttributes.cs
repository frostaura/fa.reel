namespace FrostAura.Reel.Domain.Catalog;

/// <summary>
/// LLM-derived taste attributes for one title — extracted once via OpenRouter, cached forever,
/// shared across every tenant (a major unit-economics lever). Scalars are 0–1.
/// </summary>
public class TitleAttributes
{
    public Guid TitleId { get; set; }

    public decimal Darkness { get; set; }

    /// <summary>0 = glacial slow-burn, 1 = frenetic.</summary>
    public decimal Pacing { get; set; }

    public decimal Complexity { get; set; }

    public decimal EmotionalIntensity { get; set; }

    public decimal Humor { get; set; }

    public decimal Optimism { get; set; }

    /// <summary>0 = single-protagonist, 1 = broad ensemble.</summary>
    public decimal EnsembleVsSolo { get; set; }

    /// <summary>Dominant tone keyword, e.g. "melancholic", "playful".</summary>
    public string? Tone { get; set; }

    /// <summary>Setting era keyword, e.g. "near-future", "period-victorian".</summary>
    public string? Era { get; set; }

    public string[] Themes { get; set; } = [];

    public string ExtractorModel { get; set; } = string.Empty;

    public int ExtractorVersion { get; set; }

    /// <summary>Full structured LLM output (jsonb) for audit/re-derivation.</summary>
    public string? RawJson { get; set; }

    public AttributeExtractionStatus Status { get; set; } = AttributeExtractionStatus.Pending;

    public int AttemptCount { get; set; }

    public DateTime? ExtractedAt { get; set; }
}
