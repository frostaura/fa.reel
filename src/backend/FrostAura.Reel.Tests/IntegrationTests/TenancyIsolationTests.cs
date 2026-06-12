using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Library;
using FrostAura.Reel.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

namespace FrostAura.Reel.Tests.IntegrationTests;

[Collection("postgres")]
public class TenancyIsolationTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Account_scoped_rows_are_invisible_across_accounts()
    {
        if (!fixture.Available)
        {
            return; // no Postgres available — covered in CI's backend-integration job
        }

        var unscoped = new AccountContext();
        Guid accountA, accountB, titleId;

        await using (var db = fixture.CreateContext(unscoped))
        {
            var a = new Account { Id = Guid.NewGuid(), TraktUserSlug = $"user-a-{Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow, PipelineStageChangedAt = DateTime.UtcNow };
            var b = new Account { Id = Guid.NewGuid(), TraktUserSlug = $"user-b-{Guid.NewGuid():N}", CreatedAt = DateTime.UtcNow, LastSeenAt = DateTime.UtcNow, PipelineStageChangedAt = DateTime.UtcNow };
            var title = new Title { Id = Guid.NewGuid(), MediaType = MediaType.Movie, TraktId = Random.Shared.NextInt64(1, long.MaxValue), TraktSlug = "x", Name = "Isolation Probe", CreatedAt = DateTime.UtcNow };
            db.AddRange(a, b, title);
            db.WatchedTitles.Add(new WatchedTitle { Id = Guid.NewGuid(), AccountId = a.Id, TitleId = title.Id, Plays = 1, IsFullyWatched = true, FirstSyncedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            db.WatchedTitles.Add(new WatchedTitle { Id = Guid.NewGuid(), AccountId = b.Id, TitleId = title.Id, Plays = 2, IsFullyWatched = false, FirstSyncedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
            (accountA, accountB, titleId) = (a.Id, b.Id, title.Id);
        }

        // Scoped to A: must see exactly A's row — and the global catalog title.
        var contextA = new AccountContext();
        contextA.SetAccount(accountA);
        await using (var db = fixture.CreateContext(contextA))
        {
            var watched = await db.WatchedTitles.Where(w => w.TitleId == titleId).ToListAsync();
            var row = Assert.Single(watched);
            Assert.Equal(accountA, row.AccountId);
            Assert.True(await db.Titles.AnyAsync(t => t.Id == titleId));
        }

        // Scoped to B: sees only B's row.
        var contextB = new AccountContext();
        contextB.SetAccount(accountB);
        await using (var db = fixture.CreateContext(contextB))
        {
            var watched = await db.WatchedTitles.Where(w => w.TitleId == titleId).ToListAsync();
            var row = Assert.Single(watched);
            Assert.Equal(accountB, row.AccountId);
            Assert.Equal(2, row.Plays);
        }

        // Unscoped (background/global): sees both.
        await using (var db2 = fixture.CreateContext(new AccountContext()))
        {
            Assert.Equal(2, await db2.WatchedTitles.CountAsync(w => w.TitleId == titleId));
        }
    }

    [Fact]
    public async Task Vector_roundtrip_and_cosine_distance_work()
    {
        if (!fixture.Available)
        {
            return;
        }

        await using var db = fixture.CreateContext(new AccountContext());

        var near = new Title { Id = Guid.NewGuid(), MediaType = MediaType.Movie, TraktId = Random.Shared.NextInt64(1, long.MaxValue), TraktSlug = "near", Name = "Near", CreatedAt = DateTime.UtcNow };
        var far = new Title { Id = Guid.NewGuid(), MediaType = MediaType.Movie, TraktId = Random.Shared.NextInt64(1, long.MaxValue), TraktSlug = "far", Name = "Far", CreatedAt = DateTime.UtcNow };
        db.AddRange(near, far);

        var nearVec = new float[384];
        var farVec = new float[384];
        nearVec[0] = 1f;                 // ≈ query direction
        farVec[383] = 1f;                // orthogonal
        db.TitleEmbeddings.Add(new TitleEmbedding { TitleId = near.Id, Embedding = new Vector(nearVec), EmbeddingModel = "test", SourceTextHash = "h1", CreatedAt = DateTime.UtcNow });
        db.TitleEmbeddings.Add(new TitleEmbedding { TitleId = far.Id, Embedding = new Vector(farVec), EmbeddingModel = "test", SourceTextHash = "h2", CreatedAt = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var query = new float[384];
        query[0] = 1f;
        var queryVec = new Vector(query);

        var ranked = await db.TitleEmbeddings
            .Where(e => e.TitleId == near.Id || e.TitleId == far.Id)
            .OrderBy(e => e.Embedding.CosineDistance(queryVec))
            .Select(e => e.TitleId)
            .ToListAsync();

        Assert.Equal(near.Id, ranked[0]);
    }
}
