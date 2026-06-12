using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;
using Pgvector;

#nullable disable

namespace FrostAura.Reel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:vector", ",,");

            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TraktUserSlug = table.Column<string>(type: "text", nullable: false),
                    TraktUsername = table.Column<string>(type: "text", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    AvatarUrl = table.Column<string>(type: "text", nullable: true),
                    Region = table.Column<string>(type: "text", nullable: false),
                    Tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PipelineStage = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PipelineStageChangedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Settings = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AccountTasteProfiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    LovedCentroid = table.Column<Vector>(type: "vector(384)", nullable: true),
                    RecentCentroid = table.Column<Vector>(type: "vector(384)", nullable: true),
                    ProfileJson = table.Column<string>(type: "jsonb", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountTasteProfiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ContentFilters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentFilters", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FriendlyName = table.Column<string>(type: "text", nullable: true),
                    Xml = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Episodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonNumber = table.Column<int>(type: "integer", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: true),
                    AiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RuntimeMinutes = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Episodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EvaluationResults",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrainingRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelPrecisionAt10 = table.Column<decimal>(type: "numeric", nullable: false),
                    BaselinePrecisionAt10 = table.Column<decimal>(type: "numeric", nullable: false),
                    RelativeImprovement = table.Column<decimal>(type: "numeric", nullable: false),
                    Rmse = table.Column<decimal>(type: "numeric", nullable: false),
                    Mae = table.Column<decimal>(type: "numeric", nullable: false),
                    SpearmanRho = table.Column<decimal>(type: "numeric", nullable: false),
                    HoldoutPositiveCount = table.Column<int>(type: "integer", nullable: false),
                    LowSample = table.Column<bool>(type: "boolean", nullable: false),
                    PassedGate = table.Column<bool>(type: "boolean", nullable: false),
                    DetailJson = table.Column<string>(type: "jsonb", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationResults", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ExternalApiUsages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Day = table.Column<DateTime>(type: "date", nullable: false),
                    CallCount = table.Column<long>(type: "bigint", nullable: false),
                    TokensUsed = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExternalApiUsages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeedItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeedSnapshotId = table.Column<Guid>(type: "uuid", nullable: false),
                    Row = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AnchorTitleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Rank = table.Column<int>(type: "integer", nullable: false),
                    TitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PredictedRating = table.Column<decimal>(type: "numeric", nullable: false),
                    FinalScore = table.Column<decimal>(type: "numeric", nullable: false),
                    WhyThisSentence = table.Column<string>(type: "text", nullable: false),
                    ExplanationJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FeedSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ManagedListItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RemovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RemovalReason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ManagedListItems", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ModelArtifacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    Algo = table.Column<string>(type: "text", nullable: false),
                    ArtifactBytes = table.Column<byte[]>(type: "bytea", nullable: false),
                    FeatureSchemaJson = table.Column<string>(type: "jsonb", nullable: false),
                    TrainingRunId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TrainedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ModelArtifacts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PersonAffinities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Affinity = table.Column<decimal>(type: "numeric", nullable: false),
                    RatedTitleCount = table.Column<int>(type: "integer", nullable: false),
                    ComputedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonAffinities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Persons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TmdbId = table.Column<long>(type: "bigint", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    KnownForDepartment = table.Column<string>(type: "text", nullable: true),
                    ProfilePath = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Persons", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProviderLinkPatterns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Region = table.Column<string>(type: "text", nullable: true),
                    UrlTemplate = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    IsHealthy = table.Column<bool>(type: "boolean", nullable: false),
                    ConsecutiveFailures = table.Column<int>(type: "integer", nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastHttpStatus = table.Column<int>(type: "integer", nullable: true),
                    CanaryTitleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProviderLinkPatterns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RefreshSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReplacedBySessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    UserAgent = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshSessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ShowWatchProgresses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    WatchedEpisodeCount = table.Column<int>(type: "integer", nullable: false),
                    TotalAiredEpisodes = table.Column<int>(type: "integer", nullable: false),
                    CompletionPct = table.Column<decimal>(type: "numeric", nullable: false),
                    NextEpisodeSeason = table.Column<int>(type: "integer", nullable: true),
                    NextEpisodeNumber = table.Column<int>(type: "integer", nullable: true),
                    NextEpisodeAiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastWatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ResumeLikelihood = table.Column<decimal>(type: "numeric", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShowWatchProgresses", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "StreamingProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TmdbProviderId = table.Column<int>(type: "integer", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    LogoPath = table.Column<string>(type: "text", nullable: true),
                    DisplayPriority = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StreamingProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncJobs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Priority = table.Column<short>(type: "smallint", nullable: false),
                    EnqueuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    HeartbeatAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    CursorJson = table.Column<string>(type: "jsonb", nullable: true),
                    ProgressPct = table.Column<decimal>(type: "numeric", nullable: true),
                    ProgressMessage = table.Column<string>(type: "text", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TitleAttributes",
                columns: table => new
                {
                    TitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Darkness = table.Column<decimal>(type: "numeric", nullable: false),
                    Pacing = table.Column<decimal>(type: "numeric", nullable: false),
                    Complexity = table.Column<decimal>(type: "numeric", nullable: false),
                    EmotionalIntensity = table.Column<decimal>(type: "numeric", nullable: false),
                    Humor = table.Column<decimal>(type: "numeric", nullable: false),
                    Optimism = table.Column<decimal>(type: "numeric", nullable: false),
                    EnsembleVsSolo = table.Column<decimal>(type: "numeric", nullable: false),
                    Tone = table.Column<string>(type: "text", nullable: true),
                    Era = table.Column<string>(type: "text", nullable: true),
                    Themes = table.Column<string[]>(type: "text[]", nullable: false),
                    ExtractorModel = table.Column<string>(type: "text", nullable: false),
                    ExtractorVersion = table.Column<int>(type: "integer", nullable: false),
                    RawJson = table.Column<string>(type: "jsonb", nullable: true),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    ExtractedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TitleAttributes", x => x.TitleId);
                });

            migrationBuilder.CreateTable(
                name: "TitleAvailabilities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Region = table.Column<string>(type: "text", nullable: false),
                    ProviderId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TitleAvailabilities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TitleCredits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CastOrder = table.Column<int>(type: "integer", nullable: true),
                    CharacterName = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TitleCredits", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TitleEmbeddings",
                columns: table => new
                {
                    TitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Embedding = table.Column<Vector>(type: "vector(384)", nullable: false),
                    EmbeddingModel = table.Column<string>(type: "text", nullable: false),
                    SourceTextHash = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TitleEmbeddings", x => x.TitleId);
                });

            migrationBuilder.CreateTable(
                name: "Titles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    TraktId = table.Column<long>(type: "bigint", nullable: false),
                    TraktSlug = table.Column<string>(type: "text", nullable: false),
                    ImdbId = table.Column<string>(type: "text", nullable: true),
                    TmdbId = table.Column<long>(type: "bigint", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    OriginalName = table.Column<string>(type: "text", nullable: true),
                    Year = table.Column<int>(type: "integer", nullable: true),
                    Overview = table.Column<string>(type: "text", nullable: true),
                    Tagline = table.Column<string>(type: "text", nullable: true),
                    RuntimeMinutes = table.Column<int>(type: "integer", nullable: true),
                    Certification = table.Column<string>(type: "text", nullable: true),
                    Country = table.Column<string>(type: "text", nullable: true),
                    Language = table.Column<string>(type: "text", nullable: true),
                    Genres = table.Column<string[]>(type: "text[]", nullable: false),
                    Subgenres = table.Column<string[]>(type: "text[]", nullable: false),
                    TraktRating = table.Column<decimal>(type: "numeric", nullable: true),
                    TraktVotes = table.Column<int>(type: "integer", nullable: false),
                    TmdbPopularity = table.Column<decimal>(type: "numeric", nullable: true),
                    TmdbVoteAverage = table.Column<decimal>(type: "numeric", nullable: true),
                    TmdbVoteCount = table.Column<int>(type: "integer", nullable: false),
                    ReleasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstAiredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<string>(type: "text", nullable: true),
                    Network = table.Column<string>(type: "text", nullable: true),
                    AiredEpisodes = table.Column<int>(type: "integer", nullable: true),
                    TrailerUrl = table.Column<string>(type: "text", nullable: true),
                    PosterPath = table.Column<string>(type: "text", nullable: true),
                    BackdropPath = table.Column<string>(type: "text", nullable: true),
                    LastMetadataRefreshAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Titles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TitleScores",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    ModelArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    PredictedRating = table.Column<decimal>(type: "numeric", nullable: false),
                    ContributionsJson = table.Column<string>(type: "jsonb", nullable: false),
                    ScoredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TitleScores", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrainingRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Iteration = table.Column<int>(type: "integer", nullable: false),
                    ConfigHash = table.Column<string>(type: "text", nullable: false),
                    HyperparamsJson = table.Column<string>(type: "jsonb", nullable: false),
                    SplitAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TrainRowCount = table.Column<int>(type: "integer", nullable: false),
                    HoldoutRowCount = table.Column<int>(type: "integer", nullable: false),
                    PositiveThreshold = table.Column<decimal>(type: "numeric", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrainingRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TraktConnections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    AccessTokenEncrypted = table.Column<string>(type: "text", nullable: false),
                    RefreshTokenEncrypted = table.Column<string>(type: "text", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Scope = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    LastRefreshAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastDeltaSyncAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastFullReconcileAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastActivitiesJson = table.Column<string>(type: "jsonb", nullable: true),
                    ManagedListTraktId = table.Column<long>(type: "bigint", nullable: true),
                    ManagedListSlug = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TraktConnections", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TraktOutbox",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EnqueuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    NextAttemptAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TraktOutbox", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserRatings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    SeasonNumber = table.Column<int>(type: "integer", nullable: false),
                    EpisodeNumber = table.Column<int>(type: "integer", nullable: false),
                    Rating = table.Column<short>(type: "smallint", nullable: false),
                    RatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRatings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserTitleReactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Reason = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTitleReactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WatchedTitles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AccountId = table.Column<Guid>(type: "uuid", nullable: false),
                    TitleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Plays = table.Column<int>(type: "integer", nullable: false),
                    LastWatchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsFullyWatched = table.Column<bool>(type: "boolean", nullable: false),
                    FirstSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WatchedTitles", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accounts_TraktUserSlug",
                table: "Accounts",
                column: "TraktUserSlug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AccountTasteProfiles_AccountId",
                table: "AccountTasteProfiles",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContentFilters_AccountId_Kind_Value",
                table: "ContentFilters",
                columns: new[] { "AccountId", "Kind", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Episodes_TitleId_SeasonNumber_EpisodeNumber",
                table: "Episodes",
                columns: new[] { "TitleId", "SeasonNumber", "EpisodeNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationResults_TrainingRunId",
                table: "EvaluationResults",
                column: "TrainingRunId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ExternalApiUsages_Provider_Day",
                table: "ExternalApiUsages",
                columns: new[] { "Provider", "Day" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeedItems_FeedSnapshotId_Row_Rank",
                table: "FeedItems",
                columns: new[] { "FeedSnapshotId", "Row", "Rank" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedSnapshots_AccountId",
                table: "FeedSnapshots",
                column: "AccountId",
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_ManagedListItems_AccountId_TitleId",
                table: "ManagedListItems",
                columns: new[] { "AccountId", "TitleId" },
                unique: true,
                filter: "\"RemovedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ModelArtifacts_AccountId",
                table: "ModelArtifacts",
                column: "AccountId",
                unique: true,
                filter: "\"Status\" = 'Active'");

            migrationBuilder.CreateIndex(
                name: "IX_ModelArtifacts_AccountId_Version",
                table: "ModelArtifacts",
                columns: new[] { "AccountId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PersonAffinities_AccountId_PersonId_Role",
                table: "PersonAffinities",
                columns: new[] { "AccountId", "PersonId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Persons_Name",
                table: "Persons",
                column: "Name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Persons_TmdbId",
                table: "Persons",
                column: "TmdbId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProviderLinkPatterns_ProviderId_Region",
                table: "ProviderLinkPatterns",
                columns: new[] { "ProviderId", "Region" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshSessions_AccountId",
                table: "RefreshSessions",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_RefreshSessions_TokenHash",
                table: "RefreshSessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShowWatchProgresses_AccountId_TitleId",
                table: "ShowWatchProgresses",
                columns: new[] { "AccountId", "TitleId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StreamingProviders_TmdbProviderId",
                table: "StreamingProviders",
                column: "TmdbProviderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobs_AccountId_Kind",
                table: "SyncJobs",
                columns: new[] { "AccountId", "Kind" },
                unique: true,
                filter: "\"Status\" IN ('Pending', 'Running')");

            migrationBuilder.CreateIndex(
                name: "IX_SyncJobs_Status_Priority_EnqueuedAt",
                table: "SyncJobs",
                columns: new[] { "Status", "Priority", "EnqueuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TitleAttributes_Status",
                table: "TitleAttributes",
                column: "Status",
                filter: "\"Status\" = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_TitleAvailabilities_TitleId_Region",
                table: "TitleAvailabilities",
                columns: new[] { "TitleId", "Region" });

            migrationBuilder.CreateIndex(
                name: "IX_TitleAvailabilities_TitleId_Region_ProviderId_Kind",
                table: "TitleAvailabilities",
                columns: new[] { "TitleId", "Region", "ProviderId", "Kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TitleCredits_PersonId",
                table: "TitleCredits",
                column: "PersonId");

            migrationBuilder.CreateIndex(
                name: "IX_TitleCredits_TitleId_PersonId_Role",
                table: "TitleCredits",
                columns: new[] { "TitleId", "PersonId", "Role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TitleEmbeddings_Embedding",
                table: "TitleEmbeddings",
                column: "Embedding")
                .Annotation("Npgsql:IndexMethod", "hnsw")
                .Annotation("Npgsql:IndexOperators", new[] { "vector_cosine_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Titles_Genres",
                table: "Titles",
                column: "Genres")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "IX_Titles_MediaType_TmdbId",
                table: "Titles",
                columns: new[] { "MediaType", "TmdbId" },
                unique: true,
                filter: "\"TmdbId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Titles_MediaType_TraktId",
                table: "Titles",
                columns: new[] { "MediaType", "TraktId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Titles_Name",
                table: "Titles",
                column: "Name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Titles_TmdbPopularity",
                table: "Titles",
                column: "TmdbPopularity",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "IX_TitleScores_AccountId_ModelArtifactId_PredictedRating",
                table: "TitleScores",
                columns: new[] { "AccountId", "ModelArtifactId", "PredictedRating" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "IX_TitleScores_AccountId_TitleId_ModelArtifactId",
                table: "TitleScores",
                columns: new[] { "AccountId", "TitleId", "ModelArtifactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrainingRuns_AccountId_StartedAt",
                table: "TrainingRuns",
                columns: new[] { "AccountId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TraktConnections_AccountId",
                table: "TraktConnections",
                column: "AccountId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TraktOutbox_Status_NextAttemptAt",
                table: "TraktOutbox",
                columns: new[] { "Status", "NextAttemptAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserRatings_AccountId_RatedAt",
                table: "UserRatings",
                columns: new[] { "AccountId", "RatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_UserRatings_AccountId_SubjectType_TitleId_SeasonNumber_Epis~",
                table: "UserRatings",
                columns: new[] { "AccountId", "SubjectType", "TitleId", "SeasonNumber", "EpisodeNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserTitleReactions_AccountId_TitleId_Kind",
                table: "UserTitleReactions",
                columns: new[] { "AccountId", "TitleId", "Kind" },
                unique: true,
                filter: "\"RevokedAt\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_WatchedTitles_AccountId_IsFullyWatched",
                table: "WatchedTitles",
                columns: new[] { "AccountId", "IsFullyWatched" });

            migrationBuilder.CreateIndex(
                name: "IX_WatchedTitles_AccountId_TitleId",
                table: "WatchedTitles",
                columns: new[] { "AccountId", "TitleId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Accounts");

            migrationBuilder.DropTable(
                name: "AccountTasteProfiles");

            migrationBuilder.DropTable(
                name: "ContentFilters");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "Episodes");

            migrationBuilder.DropTable(
                name: "EvaluationResults");

            migrationBuilder.DropTable(
                name: "ExternalApiUsages");

            migrationBuilder.DropTable(
                name: "FeedItems");

            migrationBuilder.DropTable(
                name: "FeedSnapshots");

            migrationBuilder.DropTable(
                name: "ManagedListItems");

            migrationBuilder.DropTable(
                name: "ModelArtifacts");

            migrationBuilder.DropTable(
                name: "PersonAffinities");

            migrationBuilder.DropTable(
                name: "Persons");

            migrationBuilder.DropTable(
                name: "ProviderLinkPatterns");

            migrationBuilder.DropTable(
                name: "RefreshSessions");

            migrationBuilder.DropTable(
                name: "ShowWatchProgresses");

            migrationBuilder.DropTable(
                name: "StreamingProviders");

            migrationBuilder.DropTable(
                name: "SyncJobs");

            migrationBuilder.DropTable(
                name: "TitleAttributes");

            migrationBuilder.DropTable(
                name: "TitleAvailabilities");

            migrationBuilder.DropTable(
                name: "TitleCredits");

            migrationBuilder.DropTable(
                name: "TitleEmbeddings");

            migrationBuilder.DropTable(
                name: "Titles");

            migrationBuilder.DropTable(
                name: "TitleScores");

            migrationBuilder.DropTable(
                name: "TrainingRuns");

            migrationBuilder.DropTable(
                name: "TraktConnections");

            migrationBuilder.DropTable(
                name: "TraktOutbox");

            migrationBuilder.DropTable(
                name: "UserRatings");

            migrationBuilder.DropTable(
                name: "UserTitleReactions");

            migrationBuilder.DropTable(
                name: "WatchedTitles");
        }
    }
}
