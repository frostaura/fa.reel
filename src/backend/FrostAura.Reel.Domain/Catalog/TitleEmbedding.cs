using Pgvector;

namespace FrostAura.Reel.Domain.Catalog;

/// <summary>
/// Plot/tone embedding for one title, in the same vector space as NL search queries.
/// Separate table so re-embedding is cheap and titles stay narrow; HNSW cosine index.
/// </summary>
public class TitleEmbedding
{
    public Guid TitleId { get; set; }

    public Vector Embedding { get; set; } = new(new float[384]);

    public string EmbeddingModel { get; set; } = string.Empty;

    /// <summary>SHA-256 of the embedded source text — skip re-embeds when unchanged.</summary>
    public string SourceTextHash { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
