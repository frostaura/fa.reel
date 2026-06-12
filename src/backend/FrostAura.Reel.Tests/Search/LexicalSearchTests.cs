using FrostAura.Reel.Application.Search;

namespace FrostAura.Reel.Tests.Search;

/// <summary>
/// The typo-tolerance contract behind keyless natural-language search: the exact misspellings
/// users actually type must land on the concept ("medevil" → "medieval"), while unrelated
/// words must not be folded into concepts they don't mean.
/// </summary>
public class LexicalSearchTests
{
    [Theory]
    [InlineData("medevil", "medieval", true)]   // the founder's own query
    [InlineData("medieval", "medieval", true)]
    [InlineData("mediival", "medieval", true)]
    [InlineData("funy", "funny", true)]
    [InlineData("scarry", "scary", true)]
    [InlineData("romantik", "romantic", true)]
    [InlineData("zombe", "zombie", true)]
    [InlineData("dark", "dragon", false)]       // unrelated words stay apart
    [InlineData("war", "western", false)]
    [InlineData("space", "scary", false)]
    public void Edit_distance_tolerance_matches_what_users_mean(string typed, string concept, bool shouldMatch)
    {
        var distance = LexicalSearchService.EditDistance(typed, concept);
        var threshold = concept.Length >= 8 ? 3 : concept.Length >= 5 ? 2 : 1;

        Assert.Equal(shouldMatch, distance <= threshold);
    }

    [Fact]
    public void Edit_distance_is_symmetric_and_zero_on_identity()
    {
        Assert.Equal(0, LexicalSearchService.EditDistance("heist", "heist"));
        Assert.Equal(
            LexicalSearchService.EditDistance("medevil", "medieval"),
            LexicalSearchService.EditDistance("medieval", "medevil"));
    }

    [Fact]
    public void Edit_distance_short_circuits_on_large_length_gap()
    {
        Assert.Equal(int.MaxValue, LexicalSearchService.EditDistance("war", "documentary"));
    }
}
