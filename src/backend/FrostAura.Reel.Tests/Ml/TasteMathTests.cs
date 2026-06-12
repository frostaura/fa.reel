using FrostAura.Reel.Application.Ml;

namespace FrostAura.Reel.Tests.Ml;

public class TasteMathTests
{
    [Fact]
    public void Shrunken_mean_pulls_small_samples_toward_the_prior()
    {
        // One lucky 10 from a user whose mean is 7 must not mint a 10.0 affinity.
        var single10 = TasteMath.ShrunkenMean([10], priorMean: 7m);
        Assert.True(single10 > 7m && single10 < 8.5m);

        // Plenty of evidence converges on the sample mean.
        var many10 = TasteMath.ShrunkenMean([.. Enumerable.Repeat((short)10, 50)], priorMean: 7m);
        Assert.True(many10 > 9.5m);

        // No evidence = the prior.
        Assert.Equal(7m, TasteMath.ShrunkenMean([], priorMean: 7m));
    }

    [Fact]
    public void Recency_weight_halves_per_half_life()
    {
        var asOf = new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc);
        Assert.Equal(1d, TasteMath.RecencyWeight(asOf, asOf), 3);
        Assert.Equal(0.5d, TasteMath.RecencyWeight(asOf.AddDays(-365), asOf), 2);
        Assert.Equal(0.25d, TasteMath.RecencyWeight(asOf.AddDays(-730), asOf), 2);
        // Future-dated ratings (clock skew) never exceed weight 1 — no leakage amplification.
        Assert.Equal(1d, TasteMath.RecencyWeight(asOf.AddDays(10), asOf), 3);
    }

    [Fact]
    public void Recency_weighted_mean_tracks_the_newer_taste()
    {
        var asOf = new DateTime(2026, 6, 12, 0, 0, 0, DateTimeKind.Utc);
        // Old love (9s, three years ago) vs recent meh (6s, this month).
        var ratings = new List<(short, DateTime)>
        {
            (9, asOf.AddDays(-1100)), (9, asOf.AddDays(-1050)),
            (6, asOf.AddDays(-20)), (6, asOf.AddDays(-10)),
        };
        var weighted = TasteMath.RecencyWeightedMean(ratings, asOf, fallback: 7m);
        var flat = ratings.Average(r => (decimal)r.Item1); // 7.5
        Assert.True(weighted < flat, $"weighted {weighted} should sit below flat {flat}");
        Assert.True(weighted < 6.8m);
    }

    [Fact]
    public void Contrarian_offset_is_signed_user_minus_crowd()
    {
        Assert.Equal(0m, TasteMath.ContrarianOffset([]));
        var offset = TasteMath.ContrarianOffset([(9, 6.5m), (8, 7.5m)]);
        Assert.Equal(1.5m, offset); // (+2.5 + 0.5) / 2
    }

    [Fact]
    public void Stable_bucket_is_deterministic_and_case_insensitive()
    {
        var a = TasteMath.StableBucket("slow-burn", 32);
        Assert.Equal(a, TasteMath.StableBucket("Slow-Burn", 32));
        Assert.InRange(a, 0, 31);
        // Distinct strings spread (not a strict guarantee, but these must not all collide).
        var buckets = new[] { "heist", "dystopian", "coming-of-age", "found-footage" }
            .Select(v => TasteMath.StableBucket(v, 32)).Distinct().Count();
        Assert.True(buckets >= 3);
    }

    [Theory]
    [InlineData("PG-13", 3f)]
    [InlineData("TV-MA", 4f)]
    [InlineData("G", 1f)]
    [InlineData(null, 0f)]
    [InlineData("weird-rating", 0f)]
    public void Certification_ordinal_maps_known_systems(string? cert, float expected)
    {
        Assert.Equal(expected, TasteMath.CertificationOrdinal(cert));
    }

    [Fact]
    public void Feature_schema_is_internally_consistent()
    {
        var names = FeatureSchema.AllNames();
        Assert.Equal(FeatureSchema.VectorLength, names.Length);
        Assert.Equal(names.Length, names.Distinct().Count());
        Assert.Contains("simLovedCentroid", names);
        Assert.Contains("genre:science-fiction", names);
    }
}
