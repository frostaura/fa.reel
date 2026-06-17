using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Domain.Search;

/// <summary>
/// A durable Ask Reel conversation — so a chat survives a reload and resumes where it left off.
/// Turns hang off it; the title is the first user message (for a future conversation list).
/// </summary>
public class SearchConversation : IAccountScoped
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    /// <summary>The opening message, truncated — a human label for the conversation.</summary>
    public string Title { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime LastTurnAt { get; set; }
}
