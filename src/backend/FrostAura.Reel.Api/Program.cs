using FrostAura.Reel.Api;
using FrostAura.Reel.Api.Endpoints;
using FrostAura.Reel.Api.Middleware;
using FrostAura.Reel.Infrastructure;
using FrostAura.Reel.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

DotEnv.Load();

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddReelInfrastructure(builder.Configuration);
builder.Services.AddOpenApi();

var corsOrigins = (builder.Configuration["CORS_ALLOWED_ORIGINS"] ?? "http://localhost:5173")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials()));

var app = builder.Build();

app.UseCors();

app.UseMiddleware<AccountResolutionMiddleware>();

app.MapOpenApi();

app.MapAuthEndpoints();
app.MapSettingsEndpoints();
app.MapSyncEndpoints();
app.MapFeedEndpoints();
app.MapTitleEndpoints();
app.MapReactionEndpoints();
app.MapMetricsEndpoints();
app.MapSseEndpoints();
app.MapDevEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapGet("/health/ready", async (ReelDbContext db, CancellationToken ct) =>
{
    var canConnect = await db.Database.CanConnectAsync(ct);
    if (!canConnect)
    {
        return Results.Problem("Database unreachable", statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var pending = (await db.Database.GetPendingMigrationsAsync(ct)).Count();
    return pending == 0
        ? Results.Ok(new { status = "ready" })
        : Results.Problem($"{pending} pending migration(s)", statusCode: StatusCodes.Status503ServiceUnavailable);
});

await DatabaseInitializer.InitializeAsync(app.Services);

app.Run();

/// <summary>Exposed for WebApplicationFactory-based integration tests.</summary>
public partial class Program;
