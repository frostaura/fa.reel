using System.Reflection;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Domain.Auth;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Feed;
using FrostAura.Reel.Domain.Filters;
using FrostAura.Reel.Domain.Library;
using FrostAura.Reel.Domain.Ml;
using FrostAura.Reel.Domain.Providers;
using FrostAura.Reel.Domain.Sync;
using FrostAura.Reel.Domain.Tenancy;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FrostAura.Reel.Infrastructure.Persistence;

/// <summary>
/// Single DbContext for catalog (global) + account-scoped data. Multi-tenancy is enforced
/// here, once: every <see cref="IAccountScoped"/> entity carries a global query filter bound
/// to the scoped <see cref="IAccountContext"/>. Unscoped contexts (background/global work)
/// see everything; HTTP requests are always pinned to the session's account.
/// </summary>
public class ReelDbContext(DbContextOptions<ReelDbContext> options, IAccountContext accountContext)
    : DbContext(options), IDataProtectionKeyContext, IReelDbContext
{
    private readonly IAccountContext _accountContext = accountContext;

    // Tenancy & auth
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<TraktConnection> TraktConnections => Set<TraktConnection>();
    public DbSet<RefreshSession> RefreshSessions => Set<RefreshSession>();

    // Global catalog
    public DbSet<Title> Titles => Set<Title>();
    public DbSet<TitleEmbedding> TitleEmbeddings => Set<TitleEmbedding>();
    public DbSet<TitleAttributes> TitleAttributes => Set<TitleAttributes>();
    public DbSet<Person> Persons => Set<Person>();
    public DbSet<TitleCredit> TitleCredits => Set<TitleCredit>();
    public DbSet<Episode> Episodes => Set<Episode>();
    public DbSet<StreamingProvider> StreamingProviders => Set<StreamingProvider>();
    public DbSet<ProviderLinkPattern> ProviderLinkPatterns => Set<ProviderLinkPattern>();
    public DbSet<TitleAvailability> TitleAvailabilities => Set<TitleAvailability>();

    // Account library
    public DbSet<WatchedTitle> WatchedTitles => Set<WatchedTitle>();
    public DbSet<ShowWatchProgress> ShowWatchProgresses => Set<ShowWatchProgress>();
    public DbSet<UserRating> UserRatings => Set<UserRating>();
    public DbSet<UserTitleReaction> UserTitleReactions => Set<UserTitleReaction>();
    public DbSet<ContentFilter> ContentFilters => Set<ContentFilter>();

    // ML
    public DbSet<PersonAffinity> PersonAffinities => Set<PersonAffinity>();
    public DbSet<AccountTasteProfile> AccountTasteProfiles => Set<AccountTasteProfile>();
    public DbSet<TrainingRun> TrainingRuns => Set<TrainingRun>();
    public DbSet<ModelArtifact> ModelArtifacts => Set<ModelArtifact>();
    public DbSet<EvaluationResult> EvaluationResults => Set<EvaluationResult>();
    public DbSet<TitleScore> TitleScores => Set<TitleScore>();

    // Feed
    public DbSet<FeedSnapshot> FeedSnapshots => Set<FeedSnapshot>();
    public DbSet<FeedItem> FeedItems => Set<FeedItem>();

    // Sync
    public DbSet<SyncJob> SyncJobs => Set<SyncJob>();
    public DbSet<TraktOutboxEntry> TraktOutbox => Set<TraktOutboxEntry>();
    public DbSet<ManagedListItem> ManagedListItems => Set<ManagedListItem>();
    public DbSet<ExternalApiUsage> ExternalApiUsages => Set<ExternalApiUsage>();

    // Data Protection key ring (encrypted Trakt tokens survive restarts)
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Enums as strings everywhere: ops-friendly (readable rows, usable in index filters).
        foreach (var enumType in typeof(Account).Assembly.GetTypes().Where(t => t.IsEnum))
        {
            configurationBuilder.Properties(enumType).HaveConversion<string>().HaveMaxLength(32);
        }

        // Npgsql rejects non-UTC DateTimes for timestamptz; external date-only payloads arrive
        // Kind=Unspecified, so normalize model-wide instead of policing every assignment.
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");
        modelBuilder.HasPostgresExtension("pg_trgm");

        // ── Tenancy ────────────────────────────────────────────────────────────────────────
        modelBuilder.Entity<Account>(e =>
        {
            e.HasIndex(a => a.TraktUserSlug).IsUnique();
            e.OwnsOne(a => a.Settings, s => s.ToJson());
        });

        modelBuilder.Entity<TraktConnection>(e =>
        {
            e.HasIndex(c => c.AccountId).IsUnique();
            e.Property(c => c.LastActivitiesJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<RefreshSession>(e =>
        {
            e.HasIndex(s => s.TokenHash).IsUnique();
            e.HasIndex(s => s.AccountId);
        });

        // ── Catalog ────────────────────────────────────────────────────────────────────────
        modelBuilder.Entity<Title>(e =>
        {
            e.HasIndex(t => new { t.MediaType, t.TraktId }).IsUnique();
            e.HasIndex(t => new { t.MediaType, t.TmdbId }).IsUnique().HasFilter("\"TmdbId\" IS NOT NULL");
            e.HasIndex(t => t.Name).HasMethod("gin").HasOperators("gin_trgm_ops");
            e.HasIndex(t => t.TmdbPopularity).IsDescending();
            e.HasIndex(t => t.Genres).HasMethod("gin");
        });

        modelBuilder.Entity<TitleEmbedding>(e =>
        {
            e.HasKey(x => x.TitleId);
            e.Property(x => x.Embedding).HasColumnType("vector(1536)");
            // HNSW over ivfflat: the catalog grows incrementally from zero rows, and HNSW
            // needs no representative data at index-build time.
            e.HasIndex(x => x.Embedding).HasMethod("hnsw").HasOperators("vector_cosine_ops");
        });

        modelBuilder.Entity<TitleAttributes>(e =>
        {
            e.HasKey(x => x.TitleId);
            e.Property(x => x.RawJson).HasColumnType("jsonb");
            // Extraction queue scan: only Pending rows are ever fetched by the worker.
            e.HasIndex(x => x.Status).HasFilter("\"Status\" = 'Pending'");
        });

        modelBuilder.Entity<Person>(e =>
        {
            e.HasIndex(p => p.TmdbId).IsUnique();
            e.HasIndex(p => p.Name).HasMethod("gin").HasOperators("gin_trgm_ops");
        });

        modelBuilder.Entity<TitleCredit>(e =>
        {
            e.HasIndex(c => new { c.TitleId, c.PersonId, c.Role }).IsUnique();
            e.HasIndex(c => c.PersonId);
        });

        modelBuilder.Entity<Episode>(e =>
        {
            e.HasIndex(x => new { x.TitleId, x.SeasonNumber, x.EpisodeNumber }).IsUnique();
        });

        modelBuilder.Entity<StreamingProvider>(e => e.HasIndex(p => p.TmdbProviderId).IsUnique());

        modelBuilder.Entity<ProviderLinkPattern>(e => e.HasIndex(p => new { p.ProviderId, p.Region }));

        modelBuilder.Entity<TitleAvailability>(e =>
        {
            e.HasIndex(a => new { a.TitleId, a.Region, a.ProviderId, a.Kind }).IsUnique();
            e.HasIndex(a => new { a.TitleId, a.Region });
        });

        // ── Library ────────────────────────────────────────────────────────────────────────
        modelBuilder.Entity<WatchedTitle>(e =>
        {
            e.HasIndex(w => new { w.AccountId, w.TitleId }).IsUnique();
            e.HasIndex(w => new { w.AccountId, w.IsFullyWatched });
        });

        modelBuilder.Entity<ShowWatchProgress>(e => e.HasIndex(p => new { p.AccountId, p.TitleId }).IsUnique());

        modelBuilder.Entity<UserRating>(e =>
        {
            e.HasIndex(r => new { r.AccountId, r.SubjectType, r.TitleId, r.SeasonNumber, r.EpisodeNumber }).IsUnique();
            e.HasIndex(r => new { r.AccountId, r.RatedAt });
        });

        modelBuilder.Entity<UserTitleReaction>(e =>
        {
            e.HasIndex(r => new { r.AccountId, r.TitleId, r.Kind }).IsUnique().HasFilter("\"RevokedAt\" IS NULL");
        });

        modelBuilder.Entity<ContentFilter>(e => e.HasIndex(f => new { f.AccountId, f.Kind, f.Value }).IsUnique());

        // ── ML ─────────────────────────────────────────────────────────────────────────────
        modelBuilder.Entity<PersonAffinity>(e => e.HasIndex(a => new { a.AccountId, a.PersonId, a.Role }).IsUnique());

        modelBuilder.Entity<AccountTasteProfile>(e =>
        {
            e.HasIndex(p => p.AccountId).IsUnique();
            e.Property(p => p.LovedCentroid).HasColumnType("vector(1536)");
            e.Property(p => p.RecentCentroid).HasColumnType("vector(1536)");
            e.Property(p => p.ProfileJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<TrainingRun>(e =>
        {
            e.HasIndex(r => new { r.AccountId, r.StartedAt });
            e.Property(r => r.HyperparamsJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<ModelArtifact>(e =>
        {
            e.HasIndex(a => new { a.AccountId, a.Version }).IsUnique();
            e.HasIndex(a => a.AccountId).IsUnique().HasFilter("\"Status\" = 'Active'");
            e.Property(a => a.FeatureSchemaJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<EvaluationResult>(e =>
        {
            e.HasIndex(r => r.TrainingRunId).IsUnique();
            e.Property(r => r.DetailJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<TitleScore>(e =>
        {
            e.HasIndex(s => new { s.AccountId, s.TitleId, s.ModelArtifactId }).IsUnique();
            e.HasIndex(s => new { s.AccountId, s.ModelArtifactId, s.PredictedRating }).IsDescending(false, false, true);
            e.Property(s => s.ContributionsJson).HasColumnType("jsonb");
        });

        // ── Feed ───────────────────────────────────────────────────────────────────────────
        modelBuilder.Entity<FeedSnapshot>(e =>
        {
            e.HasIndex(s => s.AccountId).IsUnique().HasFilter("\"Status\" = 'Active'");
        });

        modelBuilder.Entity<FeedItem>(e =>
        {
            e.HasIndex(i => new { i.FeedSnapshotId, i.Row, i.Rank });
            e.Property(i => i.ExplanationJson).HasColumnType("jsonb");
        });

        // ── Sync ───────────────────────────────────────────────────────────────────────────
        modelBuilder.Entity<SyncJob>(e =>
        {
            // No duplicate in-flight job per (account, kind). Postgres treats NULL account ids
            // as distinct — global-job dedup is the scheduler's responsibility.
            e.HasIndex(j => new { j.AccountId, j.Kind }).IsUnique().HasFilter("\"Status\" IN ('Pending', 'Running')");
            e.HasIndex(j => new { j.Status, j.Priority, j.EnqueuedAt });
            e.Property(j => j.CursorJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<TraktOutboxEntry>(e =>
        {
            e.HasIndex(o => new { o.Status, o.NextAttemptAt });
            e.Property(o => o.PayloadJson).HasColumnType("jsonb");
        });

        modelBuilder.Entity<ManagedListItem>(e =>
        {
            e.HasIndex(m => new { m.AccountId, m.TitleId }).IsUnique().HasFilter("\"RemovedAt\" IS NULL");
        });

        modelBuilder.Entity<ExternalApiUsage>(e =>
        {
            e.HasIndex(u => new { u.Provider, u.Day }).IsUnique();
            e.Property(u => u.Day).HasColumnType("date");
        });

        // ── Row-level tenancy, enforced once ───────────────────────────────────────────────
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(t => typeof(IAccountScoped).IsAssignableFrom(t.ClrType) && !t.IsOwned()))
        {
            typeof(ReelDbContext)
                .GetMethod(nameof(ApplyAccountFilter), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(entityType.ClrType)
                .Invoke(this, [modelBuilder]);
        }
    }

    private void ApplyAccountFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IAccountScoped
    {
        modelBuilder.Entity<TEntity>()
            .HasQueryFilter(e => _accountContext.AccountId == null || e.AccountId == _accountContext.AccountId);
    }
}
