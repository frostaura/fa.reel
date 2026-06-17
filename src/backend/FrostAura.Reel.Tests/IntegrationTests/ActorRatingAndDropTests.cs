using FrostAura.Reel.Application.Ml;
using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Library;
using FrostAura.Reel.Domain.Tenancy;

namespace FrostAura.Reel.Tests.IntegrationTests;

/// <summary>
/// Post-launch batch on real Postgres: the explicit-actor-rating ML blend (overrides derived
/// affinity, leakage-clean) and that a manual Dropped reaction is account-isolated.
/// </summary>
[Collection("postgres")]
public class ActorRatingAndDropTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Explicit_person_rating_overrides_derived_affinity_and_respects_asOf()
    {
        if (!fixture.Available)
        {
            return;
        }

        var accountId = await SeedAccountAsync();
        var personId = Guid.NewGuid();
        var titleId = Guid.NewGuid();

        await using (var db = fixture.CreateContext(new AccountContext()))
        {
            db.Persons.Add(new Person { Id = personId, Name = "Test Actor", TmdbId = Random.Shared.Next(1, int.MaxValue) });
            db.Add(new Title { Id = titleId, MediaType = MediaType.Movie, TraktId = Random.Shared.NextInt64(1, long.MaxValue), TraktSlug = "t", Name = "T", CreatedAt = DateTime.UtcNow });
            db.TitleCredits.Add(new TitleCredit { Id = Guid.NewGuid(), TitleId = titleId, PersonId = personId, Role = CreditRole.Actor, CastOrder = 0 });
            // The user rated this title low → derived person affinity would be ~3.
            db.UserRatings.Add(new UserRating { Id = Guid.NewGuid(), AccountId = accountId, TitleId = titleId, SubjectType = RatingSubjectType.Movie, Rating = 3, RatedAt = DateTime.UtcNow.AddDays(-10), SyncedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var asOf = DateTime.UtcNow;

        // Without an explicit rating: derived affinity reflects the low title rating.
        var derived = await PersonAffinityAsync(accountId, personId, asOf);

        // Add an explicit HIGH rating for the actor (in the past, so ≤ asOf).
        await using (var db = fixture.CreateContext(new AccountContext()))
        {
            db.UserPersonRatings.Add(new UserPersonRating
            {
                Id = Guid.NewGuid(),
                AccountId = accountId,
                PersonId = personId,
                Rating = 10,
                Source = RatingSource.Reel,
                RatedAt = DateTime.UtcNow.AddDays(-1),
                UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        }

        var blended = await PersonAffinityAsync(accountId, personId, asOf);
        Assert.True(blended > derived + 2m, $"explicit 10 should pull affinity well above the derived {derived}, got {blended}");

        // Leakage guard: an asOf BEFORE the explicit rating must ignore it.
        var beforeExplicit = await PersonAffinityAsync(accountId, personId, DateTime.UtcNow.AddDays(-5));
        Assert.Equal(derived, beforeExplicit);
    }

    [Fact]
    public async Task Dropped_reaction_is_account_isolated()
    {
        if (!fixture.Available)
        {
            return;
        }

        var a = await SeedAccountAsync();
        var b = await SeedAccountAsync();
        var titleId = Guid.NewGuid();
        await using (var db = fixture.CreateContext(new AccountContext()))
        {
            db.Add(new Title { Id = titleId, MediaType = MediaType.Show, TraktId = Random.Shared.NextInt64(1, long.MaxValue), TraktSlug = "s", Name = "S", CreatedAt = DateTime.UtcNow });
            db.UserTitleReactions.Add(new UserTitleReaction { Id = Guid.NewGuid(), AccountId = a, TitleId = titleId, Kind = ReactionKind.Dropped, CreatedAt = DateTime.UtcNow });
            await db.SaveChangesAsync();
        }

        var ctxA = new AccountContext(); ctxA.SetAccount(a);
        await using (var db = fixture.CreateContext(ctxA))
        {
            Assert.Equal(1, await CountDropped(db, titleId));
        }

        var ctxB = new AccountContext(); ctxB.SetAccount(b);
        await using (var db = fixture.CreateContext(ctxB))
        {
            Assert.Equal(0, await CountDropped(db, titleId)); // B cannot see A's drop
        }
    }

    private async Task<decimal> PersonAffinityAsync(Guid accountId, Guid personId, DateTime asOf)
    {
        var scoped = new AccountContext();
        scoped.SetAccount(accountId);
        await using var db = fixture.CreateContext(scoped);
        var builder = new FeatureVectorBuilder(db);
        var taste = await builder.BuildTasteStateAsync(accountId, asOf, CancellationToken.None);
        return taste.PersonRatings.TryGetValue(personId, out var ratings)
            ? TasteMath.ShrunkenMean(ratings, taste.UserMean)
            : taste.UserMean;
    }

    private static async Task<int> CountDropped(Application.Persistence.IReelDbContext db, Guid titleId)
    {
        return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
            db.UserTitleReactions.Where(r => r.TitleId == titleId && r.Kind == ReactionKind.Dropped && r.RevokedAt == null));
    }

    private async Task<Guid> SeedAccountAsync()
    {
        await using var db = fixture.CreateContext(new AccountContext());
        var account = new Account
        {
            Id = Guid.NewGuid(),
            TraktUserSlug = $"batch-{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow,
            LastSeenAt = DateTime.UtcNow,
            PipelineStageChangedAt = DateTime.UtcNow,
        };
        db.Add(account);
        await db.SaveChangesAsync();
        return account.Id;
    }
}
