using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FrostAura.Reel.Tests.IntegrationTests;

/// <summary>
/// Real-Postgres fixture (pgvector image required). Reads REEL_TEST_CONNECTION_STRING; when
/// absent the integration tests no-op so the plain unit run stays green without a database.
/// Migrates once per collection on a uniquely-named schema-fresh database.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public string? ConnectionString { get; } = Environment.GetEnvironmentVariable("REEL_TEST_CONNECTION_STRING");

    public bool Available => ConnectionString is not null;

    public async Task InitializeAsync()
    {
        if (!Available)
        {
            return;
        }

        await using var db = CreateContext(new AccountContext());
        await db.Database.EnsureDeletedAsync();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public ReelDbContext CreateContext(IAccountContext accountContext) =>
        new(
            new DbContextOptionsBuilder<ReelDbContext>()
                .UseNpgsql(ConnectionString!, npgsql => npgsql.UseVector())
                .Options,
            accountContext);
}

[CollectionDefinition("postgres")]
public class PostgresCollection : ICollectionFixture<PostgresFixture>;
