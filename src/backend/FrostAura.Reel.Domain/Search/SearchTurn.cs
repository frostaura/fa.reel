using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Domain.Search;

/// <summary>One turn in a persisted <see cref="SearchConversation"/> — a user message or Reel's reply.</summary>
public class SearchTurn : IAccountScoped
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid ConversationId { get; set; }

    /// <summary>"user" or "assistant".</summary>
    public string Role { get; set; } = "user";

    public string Text { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
