namespace FrostAura.Reel.Application.Search;

/// <summary>
/// Umbrella exclusion terms expand to their TMDB keyword family at filter-WRITE time, so the
/// SQL predicate stays a simple per-row match and the user sees every member as its own
/// removable chip — airtight without being opaque. TMDB tags titles with specific terms
/// ("lesbian relationship", "gay theme"), so excluding the umbrella alone misses most of them.
/// </summary>
public static class KeywordFilterExpansion
{
    private static readonly Dictionary<string, string[]> Families = new(StringComparer.OrdinalIgnoreCase)
    {
        // Any lgbt-prefixed spelling (lgbt, lgbtq, lgbtq+, lgbtqia…) → the TMDB keyword family.
        ["lgbt"] = ["lgbt", "gay", "lesbian", "transgender", "queer", "bisexual", "same-sex relationship", "drag queen"],
    };

    public static IReadOnlyList<string> Expand(IEnumerable<string> values)
    {
        var result = new List<string>();
        foreach (var raw in values)
        {
            var value = raw.Trim().ToLowerInvariant();
            if (value.Length == 0)
            {
                continue;
            }

            var family = Families.FirstOrDefault(f => value.StartsWith(f.Key, StringComparison.OrdinalIgnoreCase));
            if (family.Value is not null)
            {
                result.AddRange(family.Value);
            }
            else
            {
                result.Add(value);
            }
        }

        return result.Distinct().ToList();
    }
}
