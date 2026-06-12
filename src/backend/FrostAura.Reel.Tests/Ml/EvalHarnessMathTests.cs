using FrostAura.Reel.Application.Ml;

namespace FrostAura.Reel.Tests.Ml;

public class EvalHarnessMathTests
{
    [Fact]
    public void Precision_at_k_counts_loved_hits_in_the_top()
    {
        short[] ranked = [9, 8, 6, 10, 5, 7, 8, 4, 3, 2, 10, 10];
        // Top-10 of the ranking, threshold 8 → hits: 9, 8, 10, 8 = 4.
        Assert.Equal(0.4m, EvalHarness.PrecisionAtK(ranked, threshold: 8m, k: 10));
        // Shorter pool than k normalizes by pool size.
        Assert.Equal(0.5m, EvalHarness.PrecisionAtK([9, 5], threshold: 8m, k: 10));
        Assert.Equal(0m, EvalHarness.PrecisionAtK([], threshold: 8m, k: 10));
    }

    [Fact]
    public void Median_handles_even_and_odd_counts()
    {
        Assert.Equal(7m, EvalHarness.Median([7f]));
        Assert.Equal(7.5m, EvalHarness.Median([7f, 8f]));
        Assert.Equal(8m, EvalHarness.Median([10f, 8f, 6f]));
        Assert.Equal(0m, EvalHarness.Median([]));
    }

    [Fact]
    public void Spearman_detects_monotone_agreement_and_disagreement()
    {
        double[] x = [1, 2, 3, 4, 5];
        Assert.Equal(1d, EvalHarness.SpearmanRho(x, [10, 20, 30, 40, 50]), 3);
        Assert.Equal(-1d, EvalHarness.SpearmanRho(x, [50, 40, 30, 20, 10]), 3);

        // Ties share average ranks and keep rho in [-1, 1].
        var tied = EvalHarness.SpearmanRho([1, 2, 2, 3], [1, 2, 3, 4]);
        Assert.InRange(tied, 0.7d, 1d);

        Assert.Equal(0d, EvalHarness.SpearmanRho([1], [1]), 3);
    }
}
