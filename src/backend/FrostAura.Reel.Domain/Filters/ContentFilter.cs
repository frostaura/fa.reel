using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Domain.Filters;

public enum FilterKind
{
    ExcludeGenre,
    IncludeGenre,
    ExcludeTheme,
    ExcludeKeyword,
    MaxMaturity,
}

/// <summary>
/// One content-preference rule. None active by default; opt-in; airtight — the
/// EligibilityQueryBuilder applies these to every surface (hero, rows, search, continue-watching).
/// </summary>
public class ContentFilter : IAccountScoped
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public FilterKind Kind { get; set; }

    public string Value { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }
}
