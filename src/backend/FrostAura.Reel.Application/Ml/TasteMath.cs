namespace FrostAura.Reel.Application.Ml;

/// <summary>
/// Pure taste statistics — every function is deterministic and I/O-free so the math behind
/// the model's features is unit-tested in isolation. All inputs are expected to already be
/// filtered to the asOf window by the caller; nothing here can leak.
/// </summary>
public static class TasteMath
{
    /// <summary>
    /// Empirical-Bayes shrunken mean: pulls small samples toward the user's overall mean so
    /// one lucky 10 doesn't mint a 10.0 "affinity" for an actor seen once.
    /// (sum + k·prior) / (n + k), k = 3 by convention from the plan.
    /// </summary>
    public static decimal ShrunkenMean(IReadOnlyCollection<short> ratings, decimal priorMean, decimal k = 3m)
    {
        if (ratings.Count == 0)
        {
            return priorMean;
        }

        var sum = ratings.Sum(r => (decimal)r);
        return (sum + (k * priorMean)) / (ratings.Count + k);
    }

    /// <summary>Exponential recency weight with a configurable half-life (days).</summary>
    public static double RecencyWeight(DateTime ratedAt, DateTime asOf, double halfLifeDays = 365d)
    {
        var days = Math.Max(0d, (asOf - ratedAt).TotalDays);
        return Math.Pow(0.5, days / halfLifeDays);
    }

    /// <summary>
    /// Recency-weighted mean rating — the "current taste" estimate that drift features
    /// compare against the all-time mean.
    /// </summary>
    public static decimal RecencyWeightedMean(
        IReadOnlyCollection<(short Rating, DateTime RatedAt)> ratings, DateTime asOf, decimal fallback, double halfLifeDays = 365d)
    {
        if (ratings.Count == 0)
        {
            return fallback;
        }

        double weightedSum = 0, weightTotal = 0;
        foreach (var (rating, ratedAt) in ratings)
        {
            var w = RecencyWeight(ratedAt, asOf, halfLifeDays);
            weightedSum += w * rating;
            weightTotal += w;
        }

        return weightTotal <= 0 ? fallback : (decimal)(weightedSum / weightTotal);
    }

    /// <summary>
    /// Contrarian offset: how far the user deviates from the global crowd on the titles both
    /// have rated. Positive = the user rates above the crowd.
    /// </summary>
    public static decimal ContrarianOffset(IReadOnlyCollection<(short UserRating, decimal GlobalRating)> pairs)
    {
        if (pairs.Count == 0)
        {
            return 0m;
        }

        return pairs.Average(p => p.UserRating - p.GlobalRating);
    }

    /// <summary>Natural log of (1 + x) for heavy-tailed counts (votes, popularity); 0-safe.</summary>
    public static float Log1p(decimal value) => (float)Math.Log(1d + Math.Max(0d, (double)value));

    /// <summary>
    /// Stable bucket for hashed categorical features (subgenres/themes). Deterministic across
    /// runs and machines — string.GetHashCode is randomized per process and must never be used
    /// for features.
    /// </summary>
    public static int StableBucket(string value, int buckets)
    {
        unchecked
        {
            uint hash = 2166136261; // FNV-1a
            foreach (var ch in value)
            {
                hash ^= char.ToLowerInvariant(ch);
                hash *= 16777619;
            }

            return (int)(hash % (uint)buckets);
        }
    }

    /// <summary>Certification → coarse maturity ordinal (0 unknown … 5 adult).</summary>
    public static float CertificationOrdinal(string? certification) =>
        certification?.Trim().ToUpperInvariant() switch
        {
            "G" or "TV-G" or "TV-Y" or "TV-Y7" or "U" => 1f,
            "PG" or "TV-PG" => 2f,
            "PG-13" or "TV-14" or "12" or "12A" => 3f,
            "R" or "TV-MA" or "15" or "16" => 4f,
            "NC-17" or "18" or "X" => 5f,
            _ => 0f,
        };
}
