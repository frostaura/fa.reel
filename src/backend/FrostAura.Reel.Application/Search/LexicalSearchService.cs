using FrostAura.Reel.Domain.Catalog;
using Microsoft.EntityFrameworkCore;

namespace FrostAura.Reel.Application.Search;

/// <summary>
/// Keyless natural-language search: tokenizes the query, maps concept words to genres and
/// TMDB-keyword terms (typo-tolerant via edit distance — "medevil" finds "medieval"), then
/// scores eligible titles on genre overlap + keyword/overview/name hits. Honest and useful
/// today; the embedding-based semantic ranker transparently supersedes it when the OpenAI
/// key lands. Recommendation floors (MinPredictedRating) apply downstream.
/// </summary>
public class LexicalSearchService(EligibilityQueryBuilder eligibility)
{
    private static readonly HashSet<string> Stopwords =
    [
        "i", "a", "an", "and", "or", "the", "to", "of", "in", "on", "for", "with", "want",
        "wanna", "something", "anything", "some", "watch", "watching", "movie", "movies",
        "film", "films", "show", "shows", "series", "tonight", "please", "me", "my", "that",
        "is", "it", "like", "but", "really", "very", "kind", "sort", "feel", "feels", "bit",
    ];

    /// <summary>Concept → (genres, keyword terms). Keys are matched typo-tolerantly.</summary>
    private static readonly Dictionary<string, (string[] Genres, string[] Terms)> Concepts = new()
    {
        ["medieval"] = (["history", "fantasy", "adventure"], ["medieval", "middle ages", "knight", "kingdom", "castle", "sword"]),
        ["fun"] = (["comedy", "adventure", "family"], ["feel-good", "buddy"]),
        ["funny"] = (["comedy"], ["parody", "satire"]),
        ["light"] = (["comedy", "family"], ["feel-good"]),
        ["scary"] = (["horror", "thriller"], ["supernatural", "ghost"]),
        ["space"] = (["science-fiction"], ["space", "astronaut", "alien"]),
        ["future"] = (["science-fiction"], ["dystopia", "cyberpunk"]),
        ["romantic"] = (["romance"], ["love"]),
        ["war"] = (["war", "history"], ["battle", "soldier"]),
        ["heist"] = (["crime", "thriller"], ["heist", "robbery"]),
        ["dark"] = (["thriller", "crime", "drama"], ["neo-noir", "serial killer"]),
        ["cozy"] = (["comedy", "family", "romance"], ["feel-good", "small town"]),
        ["epic"] = (["adventure", "fantasy", "action"], ["epic", "quest"]),
        ["magic"] = (["fantasy"], ["magic", "wizard", "witch"]),
        ["detective"] = (["mystery", "crime"], ["detective", "investigation", "whodunit"]),
        ["spy"] = (["action", "thriller"], ["spy", "espionage"]),
        ["western"] = (["western"], ["cowboy", "outlaw"]),
        ["sport"] = (["sports", "drama"], ["sports", "boxing", "underdog"]),
        ["music"] = (["music", "musical"], ["musician", "band", "concert"]),
        ["animated"] = (["animation"], []),
        ["anime"] = (["anime", "animation"], ["anime"]),
        ["documentary"] = (["documentary"], []),
        ["zombie"] = (["horror"], ["zombie", "post-apocalyptic"]),
        ["superhero"] = (["superhero", "action"], ["superhero", "comic book"]),
        ["dragon"] = (["fantasy", "adventure"], ["dragon"]),
        ["pirate"] = (["adventure", "action"], ["pirate", "sea"]),
        ["robot"] = (["science-fiction"], ["robot", "artificial intelligence"]),
        ["crime"] = (["crime", "thriller"], ["gangster", "mafia"]),
        ["family"] = (["family"], ["feel-good"]),
        ["mystery"] = (["mystery"], ["whodunit", "investigation"]),
    };

    public record LexicalResult(Title Title, double MatchScore, IReadOnlyList<string> MatchedOn);

    /// <summary>One query intent ("medevil", "fun", a free-text token) and its expansion.</summary>
    private sealed record ConceptGroup(string Label, string[] Genres, string[] Terms, bool IsFreeText);

    public async Task<List<LexicalResult>> SearchAsync(Guid accountId, string query, int take, CancellationToken ct)
    {
        var tokens = query.ToLowerInvariant()
            .Split([' ', ',', '.', '!', '?', ';', ':', '\'', '"', '-'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(t => t.Length >= 3 && !Stopwords.Contains(t))
            .Distinct()
            .Take(8)
            .ToList();

        // Each surviving token becomes ONE concept group; scoring is per-group so a title
        // covering two intents ("medieval" + "fun") always outranks one that merely
        // matches a single intent's genre expansion several times over.
        var groups = new List<ConceptGroup>();
        foreach (var token in tokens)
        {
            ConceptGroup? group = null;

            // Typo-tolerant concept lookup ("medevil" → "medieval").
            foreach (var (concept, expansion) in Concepts)
            {
                if (token == concept || EditDistance(token, concept) <= MaxDistance(concept))
                {
                    group = new ConceptGroup(concept, expansion.Genres, expansion.Terms, IsFreeText: false);
                    break;
                }
            }

            // Direct genre-name hits ("thriller", "westerns").
            if (group is null)
            {
                foreach (var genre in Ml.FeatureSchema.Genres)
                {
                    if (token == genre || EditDistance(token, genre) <= MaxDistance(genre))
                    {
                        group = new ConceptGroup(genre, [genre], [], IsFreeText: false);
                        break;
                    }
                }
            }

            groups.Add(group ?? new ConceptGroup(token, [], [token], IsFreeText: true));
        }

        if (groups.Count == 0)
        {
            return [];
        }

        // Candidate pull: all groups' genres (popular first) ∪ per-term text matches.
        var candidates = new Dictionary<Guid, Title>();
        var allGenres = groups.SelectMany(g => g.Genres).Distinct().ToList();

        if (allGenres.Count > 0)
        {
            var byGenre = await eligibility.EligibleTitles(accountId)
                .Where(t => t.Genres.Any(g => allGenres.Contains(g)))
                .OrderByDescending(t => t.TmdbPopularity)
                .Take(400)
                .ToListAsync(ct);
            foreach (var title in byGenre)
            {
                candidates[title.Id] = title;
            }
        }

        foreach (var term in groups.SelectMany(g => g.Terms).Distinct().Take(8))
        {
            var byTerm = await eligibility.EligibleTitles(accountId)
                .Where(t => t.Name.ToLower().Contains(term)
                    || (t.Overview != null && t.Overview.ToLower().Contains(term))
                    || t.Keywords.Any(k => k.Contains(term)))
                .OrderByDescending(t => t.TmdbPopularity)
                .Take(120)
                .ToListAsync(ct);
            foreach (var title in byTerm)
            {
                candidates[title.Id] = title;
            }
        }

        // Per-group scoring: keyword > genre > overview > name inside a group (capped),
        // then a coverage bonus per additional group matched — AND-like semantics.
        var results = new List<LexicalResult>(candidates.Count);
        foreach (var title in candidates.Values)
        {
            var matchedOn = new List<string>();
            double score = 0;
            var groupsMatched = 0;

            foreach (var group in groups)
            {
                double groupScore = 0;

                // Keyword hit = direct evidence for the intent; label with the concrete term.
                var keywordTerm = group.Terms.FirstOrDefault(term => title.Keywords.Any(k => k.Contains(term)));
                if (keywordTerm is not null)
                {
                    groupScore += 2.8;
                    matchedOn.Add(keywordTerm);
                }

                // Genre hit = proxy evidence; label honestly with the genre that actually matched
                // (a fantasy hit must not read as "medieval" on the card).
                var genreHit = title.Genres.FirstOrDefault(group.Genres.Contains);
                if (genreHit is not null)
                {
                    groupScore += 1.8;
                    matchedOn.Add(genreHit);
                }

                if (groupScore == 0)
                {
                    var textTerm = group.Terms.FirstOrDefault(term =>
                        title.Overview?.Contains(term, StringComparison.OrdinalIgnoreCase) == true
                        || title.Name.Contains(term, StringComparison.OrdinalIgnoreCase));
                    if (textTerm is not null)
                    {
                        groupScore += group.IsFreeText ? 1.4 : 0.9;
                        matchedOn.Add(textTerm);
                    }
                }

                if (groupScore > 0)
                {
                    groupsMatched++;
                    score += Math.Min(groupScore, 4.0);
                }
            }

            if (groupsMatched > 0)
            {
                score += 2.0 * (groupsMatched - 1); // coverage: each extra intent satisfied
                score += Math.Log(1 + (double)(title.TmdbPopularity ?? 0)) * 0.05;
                results.Add(new LexicalResult(title, score, matchedOn.Distinct().Take(4).ToList()));
            }
        }

        return results
            .OrderByDescending(r => r.MatchScore)
            .Take(take)
            .ToList();
    }

    private static int MaxDistance(string word) => word.Length >= 8 ? 3 : word.Length >= 5 ? 2 : 1;

    /// <summary>Plain Levenshtein — queries are ≤8 short tokens, cost is negligible.</summary>
    public static int EditDistance(string a, string b)
    {
        if (Math.Abs(a.Length - b.Length) > 3)
        {
            return int.MaxValue;
        }

        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
        {
            previous[j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1);
                current[j] = Math.Min(Math.Min(previous[j] + 1, current[j - 1] + 1), substitution);
            }

            (previous, current) = (current, previous);
        }

        return previous[b.Length];
    }
}
