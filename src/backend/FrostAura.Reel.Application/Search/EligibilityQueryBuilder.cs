using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Filters;
using FrostAura.Reel.Domain.Library;

namespace FrostAura.Reel.Application.Search;

/// <summary>
/// THE single source of the locked eligibility rule + airtight content filters. Every surface
/// that proposes titles — feed build, hero, rows, typeahead, semantic search, similar-titles —
/// goes through this builder; a surface composing its own predicate is a bug by definition.
///
/// Eligible = NOT fully watched (never-watched ∪ in-progress) AND not actively NotInterested
/// AND inside the account's content-preference rules. Maturity ceilings compare certification
/// ordinals in memory (certification systems don't order in SQL) via <see cref="PassesMaturity"/>.
/// </summary>
public class EligibilityQueryBuilder(IReelDbContext db)
{
    public IQueryable<Title> EligibleTitles(Guid accountId)
    {
        return ContentFilteredTitles(accountId).Where(t =>
            !db.WatchedTitles.Any(w => w.AccountId == accountId && w.TitleId == t.Id && w.IsFullyWatched)
            && !db.UserTitleReactions.Any(r =>
                r.AccountId == accountId && r.TitleId == t.Id
                && r.Kind == ReactionKind.NotInterested && r.RevokedAt == null));
    }

    /// <summary>
    /// The content-preference rules alone — no watched/NotInterested logic. For surfaces that
    /// legitimately show watched titles (typeahead badges them) but must still honor every
    /// exclusion. Discovery surfaces use <see cref="EligibleTitles"/>, which composes this.
    /// </summary>
    public IQueryable<Title> ContentFilteredTitles(Guid accountId)
    {
        var query = db.Titles.Where(t =>
            !db.ContentFilters.Any(f =>
                f.AccountId == accountId && f.Kind == FilterKind.ExcludeGenre && t.Genres.Contains(f.Value))
            && !db.ContentFilters.Any(f =>
                f.AccountId == accountId && f.Kind == FilterKind.ExcludeKeyword
                && (t.Name.ToLower().Contains(f.Value.ToLower())
                    || (t.Overview != null && t.Overview.ToLower().Contains(f.Value.ToLower()))
                    // Both directions: TMDB keyword contains the filter ("lgbt themes" ⊇ "lgbt"),
                    // or the filter is the longer form of the keyword ("lgbtq" ⊇ "lgbt"). The
                    // length guard stops short keywords from swallowing unrelated filters.
                    || t.Keywords.Any(k => k.Contains(f.Value.ToLower())
                        || (k.Length >= 4 && f.Value.ToLower().StartsWith(k))))));

        // Include-genres are an allowlist when present: at least one must match.
        query = query.Where(t =>
            !db.ContentFilters.Any(f => f.AccountId == accountId && f.Kind == FilterKind.IncludeGenre)
            || t.Genres.Any(g => db.ContentFilters.Any(f =>
                f.AccountId == accountId && f.Kind == FilterKind.IncludeGenre && f.Value == g)));

        return query;
    }

    /// <summary>In-memory maturity gate — apply after materializing (certification ordering is app logic).</summary>
    public static bool PassesMaturity(Title title, string? maturityCeiling)
    {
        if (string.IsNullOrWhiteSpace(maturityCeiling))
        {
            return true;
        }

        var ceiling = Ml.TasteMath.CertificationOrdinal(maturityCeiling);
        if (ceiling <= 0)
        {
            return true;
        }

        var ordinal = Ml.TasteMath.CertificationOrdinal(title.Certification);
        // Unknown certifications pass only when the ceiling allows adult content — a kids'
        // profile must never see an unrated title by accident.
        return ordinal == 0 ? ceiling >= 4 : ordinal <= ceiling;
    }
}
