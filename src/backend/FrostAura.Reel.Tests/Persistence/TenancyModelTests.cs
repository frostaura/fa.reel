using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Domain.Tenancy;
using FrostAura.Reel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FrostAura.Reel.Tests.Persistence;

/// <summary>
/// Offline model checks — the multi-tenancy contract is structural, so these tests fail the
/// build if someone adds an account-scoped entity without isolation.
/// </summary>
public class TenancyModelTests
{
    private static ReelDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<ReelDbContext>()
                .UseNpgsql("Host=localhost;Database=model_only", npgsql => npgsql.UseVector())
                .Options,
            new AccountContext());

    [Fact]
    public void Every_account_scoped_entity_has_a_query_filter()
    {
        using var db = CreateContext();

        var scopedWithoutFilter = db.Model.GetEntityTypes()
            .Where(t => typeof(IAccountScoped).IsAssignableFrom(t.ClrType) && !t.IsOwned())
            .Where(t => t.GetDeclaredQueryFilters().Count == 0)
            .Select(t => t.ClrType.Name)
            .ToList();

        Assert.Empty(scopedWithoutFilter);
    }

    [Fact]
    public void Catalog_entities_are_not_account_scoped()
    {
        // The shared catalog is the unit-economics lever (one LLM extraction serves every
        // tenant) — accidentally scoping it would silently fork the catalog per account.
        var catalogTypes = new[]
        {
            typeof(Domain.Catalog.Title),
            typeof(Domain.Catalog.TitleEmbedding),
            typeof(Domain.Catalog.TitleAttributes),
            typeof(Domain.Catalog.Person),
            typeof(Domain.Catalog.TitleCredit),
            typeof(Domain.Catalog.Episode),
            typeof(Domain.Providers.StreamingProvider),
            typeof(Domain.Providers.ProviderLinkPattern),
            typeof(Domain.Providers.TitleAvailability),
        };

        foreach (var type in catalogTypes)
        {
            Assert.False(typeof(IAccountScoped).IsAssignableFrom(type), $"{type.Name} must not be account-scoped");
        }
    }

    [Fact]
    public void Model_builds_with_all_expected_tables()
    {
        using var db = CreateContext();
        var tableCount = db.Model.GetEntityTypes().Count(t => !t.IsOwned());
        Assert.Equal(32, tableCount); // +UserPersonRating, +UserPreferenceTag (2026-06-17)
    }
}
