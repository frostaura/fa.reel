using FrostAura.Reel.Application.Search;

namespace FrostAura.Reel.Tests.Search;

public class KeywordFilterExpansionTests
{
    [Theory]
    [InlineData("lgbt")]
    [InlineData("lgbtq")]
    [InlineData("LGBTQ+")]
    [InlineData("lgbtqia")]
    public void Umbrella_spellings_expand_to_the_tmdb_keyword_family(string typed)
    {
        var expanded = KeywordFilterExpansion.Expand([typed]);

        Assert.Contains("lgbt", expanded);
        Assert.Contains("lesbian", expanded);     // catches "lesbian relationship" via containment
        Assert.Contains("gay", expanded);
        Assert.Contains("transgender", expanded);
    }

    [Fact]
    public void Non_umbrella_terms_pass_through_untouched()
    {
        var expanded = KeywordFilterExpansion.Expand(["zombie", "  Shark  "]);

        Assert.Equal(["zombie", "shark"], expanded);
    }

    [Fact]
    public void Family_members_dedupe_when_user_lists_several_spellings()
    {
        var expanded = KeywordFilterExpansion.Expand(["lgbt", "lgbtq", "gay"]);

        Assert.Equal(expanded.Distinct().Count(), expanded.Count);
    }
}
