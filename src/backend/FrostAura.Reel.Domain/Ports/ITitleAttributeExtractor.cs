namespace FrostAura.Reel.Domain.Ports;

/// <summary>
/// LLM-derived taste-attribute extraction for one title (OpenRouter, model
/// <c>openai/gpt-5.4-mini</c> — founder-locked 2026-06-12). Extracted once, cached forever,
/// shared across every tenant. Availability gates on the API key being configured OR a
/// deterministic stub being active (tests + keyless dev). One vector space with NL search by
/// construction is the embedder's job; this port supplies the structured human-legible scalars.
/// </summary>
public interface ITitleAttributeExtractor
{
    bool IsAvailable { get; }

    /// <summary>Model identifier recorded on each extraction for provenance/versioning.</summary>
    string ModelId { get; }

    /// <summary>
    /// Extracts attributes for a batch. Result is row-aligned with <paramref name="inputs"/>;
    /// a null entry means that one title could not be extracted (the caller marks it Failed and
    /// retries later). Implementations must not throw for a single bad title — only for a
    /// whole-batch transport failure.
    /// </summary>
    Task<IReadOnlyList<ExtractedTitleAttributes?>> ExtractAsync(
        IReadOnlyList<TitleAttributeInput> inputs, CancellationToken ct = default);
}

/// <summary>The metadata an extractor reasons over — everything legally available pre-extraction.</summary>
public record TitleAttributeInput(
    Guid TitleId,
    string MediaType,
    string Name,
    int? Year,
    string? Overview,
    IReadOnlyList<string> Genres,
    IReadOnlyList<string> Keywords);

/// <summary>
/// Structured extractor output. Scalars are clamped 0–1 by the adapter before return, so
/// downstream feature assembly can trust the range without re-checking.
/// </summary>
public record ExtractedTitleAttributes(
    decimal Darkness,
    decimal Pacing,
    decimal Complexity,
    decimal EmotionalIntensity,
    decimal Humor,
    decimal Optimism,
    decimal EnsembleVsSolo,
    string? Tone,
    string? Era,
    IReadOnlyList<string> Themes,
    string RawJson);
