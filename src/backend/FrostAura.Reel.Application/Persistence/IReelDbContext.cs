using FrostAura.Reel.Domain.Auth;
using FrostAura.Reel.Domain.Catalog;
using FrostAura.Reel.Domain.Feed;
using FrostAura.Reel.Domain.Filters;
using FrostAura.Reel.Domain.Library;
using FrostAura.Reel.Domain.Ml;
using FrostAura.Reel.Domain.Providers;
using FrostAura.Reel.Domain.Sync;
using FrostAura.Reel.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace FrostAura.Reel.Application.Persistence;

/// <summary>
/// Persistence port for application services — EF's DbSet/LINQ surface IS the abstraction
/// (pragmatic hexagonal: swapping ORMs is not a real risk; swapping databases is handled by
/// the provider). Implemented by ReelDbContext in Infrastructure.
/// </summary>
public interface IReelDbContext
{
    DbSet<Account> Accounts { get; }
    DbSet<TraktConnection> TraktConnections { get; }
    DbSet<RefreshSession> RefreshSessions { get; }

    DbSet<Title> Titles { get; }
    DbSet<TitleEmbedding> TitleEmbeddings { get; }
    DbSet<TitleAttributes> TitleAttributes { get; }
    DbSet<Person> Persons { get; }
    DbSet<TitleCredit> TitleCredits { get; }
    DbSet<Episode> Episodes { get; }
    DbSet<StreamingProvider> StreamingProviders { get; }
    DbSet<ProviderLinkPattern> ProviderLinkPatterns { get; }
    DbSet<TitleAvailability> TitleAvailabilities { get; }

    DbSet<WatchedTitle> WatchedTitles { get; }
    DbSet<ShowWatchProgress> ShowWatchProgresses { get; }
    DbSet<UserRating> UserRatings { get; }
    DbSet<UserTitleReaction> UserTitleReactions { get; }
    DbSet<ContentFilter> ContentFilters { get; }

    DbSet<PersonAffinity> PersonAffinities { get; }
    DbSet<AccountTasteProfile> AccountTasteProfiles { get; }
    DbSet<TrainingRun> TrainingRuns { get; }
    DbSet<ModelArtifact> ModelArtifacts { get; }
    DbSet<EvaluationResult> EvaluationResults { get; }
    DbSet<TitleScore> TitleScores { get; }

    DbSet<FeedSnapshot> FeedSnapshots { get; }
    DbSet<FeedItem> FeedItems { get; }

    DbSet<SyncJob> SyncJobs { get; }
    DbSet<TraktOutboxEntry> TraktOutbox { get; }
    DbSet<ManagedListItem> ManagedListItems { get; }
    DbSet<ExternalApiUsage> ExternalApiUsages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
