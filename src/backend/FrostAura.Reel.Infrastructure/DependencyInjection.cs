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
        services.AddHttpClient<ITraktClient, TraktClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.trakt.tv/");
            client.Timeout = TimeSpan.FromSeconds(30);
        }).AddStandardResilienceHandler();

        services.AddHttpClient<ITmdbClient, TmdbClient>(client =>
        {
            client.BaseAddress = new Uri("https://api.themoviedb.org/3/");
            client.Timeout = TimeSpan.FromSeconds(30);
        }).AddStandardResilienceHandler();

        // ── Pipeline: event hub, token store, job handlers, runner ───────────────────────
        services.AddSingleton<IPipelineEventHub, PipelineEventHub>();
        services.AddScoped<TraktTokenStore>();
        services.AddScoped<IJobHandler, FullIngestJobHandler>();
        services.AddScoped<IJobHandler, HydrateCatalogJobHandler>();
        services.AddHostedService<JobRunnerService>();

        return services;
    }
}
