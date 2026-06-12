using System.Globalization;
using FrostAura.Reel.Domain.Catalog;

namespace FrostAura.Reel.Application.Ranking;

/// <summary>
/// Deterministic why-this sentences from the model's actual top contributions — the honest
/// explainability layer behind every card (foresight DescriptionTemplater precedent; optional
/// LLM polish stays behind a flag, off by default). Never claims a reason the model didn't use.
/// </summary>
public static class ExplanationTemplater
{
    public record Context(
        Title Title,
        IReadOnlyList<(string Feature, float Contribution)> Contributions,
        string? BestPersonName,
        decimal BestPersonAffinity,
        string? TopGenre,
        string? AnchorName);

    public static string BuildSentence(Context context)
    {
        var clauses = new List<string>();

        foreach (var (feature, contribution) in context.Contributions)
        {
            if (clauses.Count >= 2)
            {
                break;
            }

            var clause = Translate(feature, contribution, context);
            if (clause is not null && !clauses.Contains(clause))
            {
                clauses.Add(clause);
            }
        }

        if (clauses.Count == 0)
        {
            clauses.Add("Strong fit with your taste profile");
        }

        if (context.AnchorName is { Length: > 0 } anchor)
        {
            clauses.Insert(0, $"Because you loved {anchor}");
        }

        return string.Join(", and ", clauses.Take(2)).TrimEnd('.') + ".";
    }

    private static string? Translate(string feature, float contribution, Context context)
    {
        if (contribution <= 0)
        {
            return null; // only positive reasons belong on a recommendation card
        }

        return feature switch
        {
            "castAffinity" or "lovedCastCount" or "directorAffinity" or "writerAffinity"
                when context.BestPersonName is { Length: > 0 } =>
                    string.Create(CultureInfo.InvariantCulture, $"you rate {context.BestPersonName} {context.BestPersonAffinity:0.0} on average"),
            "castAffinity" or "lovedCastCount" => "the cast overlaps your favourites",
            "directorAffinity" => "the director is in your top tier",
            "writerAffinity" => "written by people you rate highly",
            "userGenreAffinity" when context.TopGenre is { Length: > 0 } =>
                $"right in your {Pretty(context.TopGenre)} wheelhouse",
            "genreDrift" when context.TopGenre is { Length: > 0 } =>
                $"matches the {Pretty(context.TopGenre)} streak you've been on",
            "decadeAffinity" when context.Title.Year is { } year =>
                $"peak {(year / 10) * 10}s — your era",
            "traktRating" when context.Title.TraktRating is { } rating =>
                string.Create(CultureInfo.InvariantCulture, $"the crowd backs it too ({rating:0.0} on Trakt)"),
            "tmdbPopularityLog" => "trending right now",
            "showEngagement" => "you're already deep in this one",
            "releaseAgeLog" when IsFresh(context.Title) => "fresh off release",
            "contrarianAdjustedRating" => "scores well even by your contrarian standards",
            "simLovedCentroid" or "simRecentCentroid" or "simTopLovedMax" or "simTopLovedMean" =>
                "close in feel to the titles you love",
            _ when feature.StartsWith("genre:") => $"squarely {Pretty(feature[6..])}",
            _ => null,
        };
    }

    private static bool IsFresh(Title title)
    {
        var released = title.ReleasedAt ?? title.FirstAiredAt;
        return released is { } date && (DateTime.UtcNow - date).TotalDays <= 120;
    }

    private static string Pretty(string slug) => slug.Replace('-', ' ');
}
