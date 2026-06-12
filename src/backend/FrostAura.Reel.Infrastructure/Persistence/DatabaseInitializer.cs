using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FrostAura.Reel.Infrastructure.Persistence;

/// <summary>
/// Applies pending EF migrations on boot when Database:AutoMigrateOnStartup is true
/// (foresight pattern — first boot of a fresh compose stack provisions itself).
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseInitializer");

        if (!configuration.GetValue("Database:AutoMigrateOnStartup", false))
        {
            logger.LogInformation("Database auto-migration disabled (Database:AutoMigrateOnStartup=false).");
            return;
        }

        var db = scope.ServiceProvider.GetRequiredService<ReelDbContext>();
        var pending = (await db.Database.GetPendingMigrationsAsync(ct)).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("Database schema is current — no pending migrations.");
            return;
        }

        logger.LogInformation("Applying {Count} pending migration(s): {Migrations}", pending.Count, string.Join(", ", pending));
        await db.Database.MigrateAsync(ct);
        logger.LogInformation("Database migration complete.");
    }
}
