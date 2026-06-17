using FrostAura.Reel.Application.Auth;
using FrostAura.Reel.Application.Jobs;
using FrostAura.Reel.Application.Persistence;
using FrostAura.Reel.Application.Pipeline;
using FrostAura.Reel.Application.Sync;
using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Infrastructure.Adapters;
using FrostAura.Reel.Infrastructure.Background;
using FrostAura.Reel.Infrastructure.Persistence;
using FrostAura.Reel.Infrastructure.RateLimiting;
using FrostAura.Reel.Infrastructure.Security;
using FrostAura.Reel.Infrastructure.Sse;
using FrostAura.Reel.Infrastructure.Telemetry;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FrostAura.Reel.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Wires persistence, tenancy scope, secret protection, external API adapters, and shared rate gates.</summary>
    public static IServiceCollection AddReelInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings__Postgres is required.");

        services.AddScoped<IAccountContext, AccountContext>();

        services.AddDbContext<ReelDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));
        services.AddScoped<IReelDbContext>(sp => sp.GetRequiredService<ReelDbContext>());

        services.AddDataProtection()
            .SetApplicationName("FrostAura.Reel")
            .PersistKeysToDbContext<ReelDbContext>();

        services.AddSingleton<ISecretProtector, SecretProtector>();

        // ── Application services ───────────────────────────────────────────────────────────
        services.AddSingleton<OAuthStateCodec>();
        services.AddScoped<SessionService>();
        services.AddScoped<TraktOAuthService>();

        // ── Shared external-API rate gates (global across all tenants) ────────────────────
        var traktBudget = configuration.GetValue("TRAKT_RATE_BUDGET_PER_5MIN", 900);
        services.AddKeyedSingleton<IRateGate>("trakt", (_, _) => new PriorityRateGate(traktBudget, TimeSpan.FromMinutes(5)));
        var tmdbPerSec = configuration.GetValue("TMDB_RATE_PER_SEC", 40);
        services.AddKeyedSingleton<IRateGate>("tmdb", (_, _) => new PriorityRateGate(tmdbPerSec, TimeSpan.FromSeconds(1)));

        // ── Fair-Use usage ledger ──────────────────────────────────────────────────────────
        services.AddSingleton<ApiUsageRecorder>();
        services.AddHostedService(sp => sp.GetRequiredService<ApiUsageRecorder>());

        // ── External adapters ──────────────────────────────────────────────────────────────
        // Trakt/TMDB both front with Cloudflare, which rejects UA-less requests — HttpClient
        // sends no User-Agent by default, so an explicit one is load-bearing, not cosmetic.
        const string userAgent = "fa.reel/0.1 (+https://github.com/frostaura/fa.reel)";

        services.AddHttpClient<ITraktClient, TraktClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.trakt.tv/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        }).AddStandardResilienceHandler();

        services.AddHttpClient<ITmdbClient, TmdbClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.themoviedb.org/3/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        }).AddStandardResilienceHandler();

        services.AddHttpClient<IEmbeddingProvider, OpenAiEmbeddingProvider>(client =>
        {
            var baseUrl = configuration["EMBEDDINGS_BASE_URL"] ?? "https://api.openai.com/v1";
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        }).AddStandardResilienceHandler();

        services.AddHttpClient<ITitleAttributeExtractor, OpenRouterAttributeExtractor>(client =>
        {
            var baseUrl = configuration["OPENROUTER_BASE_URL"] ?? "https://openrouter.ai/api/v1";
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(60); // chat completions run longer than embeddings
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        }).AddStandardResilienceHandler();

        services.AddHttpClient<ISearchQueryInterpreter, OpenRouterSearchInterpreter>(client =>
        {
            var baseUrl = configuration["OPENROUTER_BASE_URL"] ?? "https://openrouter.ai/api/v1";
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        }).AddStandardResilienceHandler();

        services.AddHttpClient<ISearchAgent, OpenRouterSearchAgent>(client =>
        {
            var baseUrl = configuration["OPENROUTER_BASE_URL"] ?? "https://openrouter.ai/api/v1";
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(60);
            client.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        }).AddStandardResilienceHandler();

        // ── Shared-work coordination + request caching ───────────────────────────────────
        services.AddMemoryCache();
        services.AddSingleton<Application.Abstractions.ICatalogWorkCoordinator, Concurrency.CatalogWorkCoordinator>();

        // ── ML engine ──────────────────────────────────────────────────────────────────────
        services.AddSingleton<IModelEngine, Ml.FastTreeModelEngine>();
        services.AddScoped<Application.Ml.FeatureVectorBuilder>();
        services.AddScoped<Application.Ml.OnDemandScorer>();
        services.AddScoped<Application.Ml.EvalHarness>();

        // ── Serving pipeline ─────────────────────────────────────────────────────────────
        services.AddScoped<Application.Search.EligibilityQueryBuilder>();
        services.AddScoped<Application.Search.LexicalSearchService>();
        services.AddScoped<Application.Search.LiveSearchExpansionService>();
        services.AddScoped<Application.Ranking.CandidateGenerator>();
        services.AddScoped<Application.Ingestion.TitleHydrator>();

        // ── Pipeline: event hub, token store, ingestor, job handlers, schedulers ──────────
        services.AddSingleton<IPipelineEventHub, PipelineEventHub>();
        services.AddScoped<TraktTokenStore>();
        services.AddScoped<Application.Sync.OutboxDispatcher>();
        services.AddScoped<Application.Ingestion.TraktLibraryIngestor>();
        services.AddScoped<IJobHandler, FullIngestJobHandler>();
        services.AddScoped<IJobHandler, DeltaSyncJobHandler>();
        services.AddScoped<IJobHandler, HydrateCatalogJobHandler>();
        services.AddScoped<IJobHandler, EnrichCatalogJobHandler>();
        services.AddScoped<IJobHandler, TrainJobHandler>();
        services.AddScoped<IJobHandler, EvaluateJobHandler>();
        services.AddScoped<IJobHandler, BuildFeedJobHandler>();
        services.AddScoped<IJobHandler, RefreshAvailabilityJobHandler>();
        services.AddHostedService<JobRunnerService>();
        services.AddHostedService<TraktDeltaPollService>();
        services.AddHostedService<NightlyReconcileService>();
        services.AddHostedService<CatalogGrowthService>();
        services.AddHostedService<TokenRefreshService>();
        services.AddHostedService<TraktOutboxDispatcherService>();

        return services;
    }
}
