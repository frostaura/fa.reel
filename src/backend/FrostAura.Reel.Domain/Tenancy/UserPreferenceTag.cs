using Pgvector;

namespace FrostAura.Reel.Domain.Tenancy;

/// <summary>
/// A positive, additive taste preference the user declares in their own words — e.g. "magical
/// serial killer mysteries", "slow-burn A24 horror". The inverse of an exclusion filter: tags
/// pull matching titles into discovery and BOOST them in ranking. The tag text is embedded once
/// (same vector space as titles) so the boost is a cosine to the tag, not a brittle keyword match.
/// </summary>
public class UserPreferenceTag : IAccountScoped
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    /// <summary>The user's phrasing, shown back as a chip.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>Embedding of <see cref="Text"/>; null when no embeddings provider was configured at create time.</summary>
    public Vector? Embedding { get; set; }

    public string? EmbeddingModel { get; set; }

    public DateTime CreatedAt { get; set; }
}
