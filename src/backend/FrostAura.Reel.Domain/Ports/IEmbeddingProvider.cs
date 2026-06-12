namespace FrostAura.Reel.Domain.Ports;

/// <summary>
/// Text-embedding port (OpenAI text-embedding-3-small @1536 behind it; founder call
/// 2026-06-12). Titles and NL search queries embed through the SAME provider — one vector
/// space by construction. Availability gates on the API key being configured.
/// </summary>
public interface IEmbeddingProvider
{
    bool IsAvailable { get; }

    /// <summary>Embeds a batch of texts; row-aligned result, normalized vectors.</summary>
    Task<float[][]> EmbedAsync(IReadOnlyList<string> texts, CancellationToken ct = default);
}
