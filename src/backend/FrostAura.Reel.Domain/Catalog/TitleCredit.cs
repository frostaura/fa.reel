namespace FrostAura.Reel.Domain.Catalog;

/// <summary>Edge between a title and a person in a specific role (affinity-feature joins).</summary>
public class TitleCredit
{
    public Guid Id { get; set; }

    public Guid TitleId { get; set; }

    public Guid PersonId { get; set; }

    public CreditRole Role { get; set; }

    /// <summary>TMDB cast order — top-billed cast carries more affinity weight.</summary>
    public int? CastOrder { get; set; }

    public string? CharacterName { get; set; }
}
