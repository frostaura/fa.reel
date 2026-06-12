using FrostAura.Reel.Application.Search;
using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Filters;
using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Tests.IntegrationTests;

/// <summary>
/// The keyless NL-search engine end-to-end on real Postgres: typo-tolerant concept queries
/// return the right titles, ranked by intent coverage; and content filters stay airtight —
/// a TMDB-keyword exclusion (the "remove lgbtq themes" case) removes a title from search
/// even when nothing else about it matches the excluded word.
/// </summary>
[Collection("postgres")]
public class LexicalSearchIntegrationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Misspelled_concept_query_finds_and_ranks_by_intent_coverage()
    {
        if (!fixture.Available)
        {
            return;
        }

        var accountId = await SeedAccountAsync();
        await SeedTitleAsync("A Knight's Tale", ["adventure", "comedy"], ["medieval", "knight", "jousting"]);
        await SeedTitleAsync("Generic Comedy", ["comedy"], ["stand-up"]);
        await SeedTitleAsync("Unrelated Drama", ["drama"], ["divorce"]);

        var results = await SearchAsync(accountId, "something medevil and fun");

        Assert.True(results.Count >= 2);
        // Medieval+fun coverage must beat fun-only — regardless of popularity.
        Assert.Equal("A Knight's Tale", results[0].Title.Name);
        Assert.DoesNotContain(results, r => r.Title.Name == "Unrelated Drama");
    }

    [Fact]
    public async Task Keyword_exclusion_filter_removes_titles_by_tmdb_keyword()
    {
        if (!fixture.Available)
        {
            return;
        }

        var accountId = await SeedAccountAsync();
        await SeedTitleAsync("Filtered Romance", ["romance"], ["lgbt", "love"]);
        await SeedTitleAsync("Kept Romance", ["romance"], ["love"]);

        await using (var db = fixture.CreateContext(new AccountContext()))
        {
            // The user types the longer form; TMDB's canonical keyword is "lgbt" — the
            // filter must still bite (forward containment fails, prefix direction catches it).
            db.ContentFilters.Add(new ContentFilter
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                Kind = FilterKind.ExcludeKeyword,
                Value = "lgbtq",
                CreatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var results = await SearchAsync(accountId, "romantic");

        Assert.Contains(results, r => r.Title.Name == "Kept Romance");
        Assert.DoesNotContain(results, r => r.Title.Name == "Filtered Romance");
    }

    private async Task<Guid> SeedAccountAsync()
    {
        await using var db = fixture.CreateContext(new AccountContext());
        var account = new Account
        {
            Id = Guid.NewGuid(),
            TraktUserSlug = $"lexical-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            PipelineStageChangedAt = DateTime.UtcNow,
        };
        db.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }

    private async Task SeedTitleAsync(string name, string[] genres, string[] keywords)
    {
        await using var db = fixture.CreateContext(new AccountContext());
        db.Add(new Title
        {
            Id = Guid.NewGuid(),
            MediaType = MediaType.Movie,
            TraktId = Random.Shared.NextInt64(1, long.MaxValue),
            TraktSlug = name.ToLowerInvariant().Replace(' ', '-'),
            Name = name,
            Genres = genres,
            Keywords = keywords,
            TmdbPopularity = 50,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private async Task<List<LexicalSearchService.LexicalResult>> SearchAsync(Guid accountId, string query)
    {
        var scoped = new AccountContext();
        scoped.SetAccount(accountId);
        await using var db = fixture.CreateContext(scoped);
        var service = new LexicalSearchService(new EligibilityQueryBuilder(db));
        return await service.SearchAsync(accountId, query, 24, CancellationToken.None);
    }
}
