using FrostAura.Reel.Application.Tenancy;
using FrostAura.Reel.Domain.Ports;
using FrostAura.Reel.Infrastructure.Persistence;
using FrostAura.Reel.Infrastructure.Security;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace FrostAura.Reel.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Wires persistence, tenancy scope, and secret protection. Adapters and background services join as they land.</summary>
    public static IServiceCollection AddReelInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("ConnectionStrings__Postgres is required.");

        services.AddScoped<IAccountContext, AccountContext>();

        services.AddDbContext<ReelDbContext>(options =>
            options.UseNpgsql(connectionString, npgsql => npgsql.UseVector()));

        services.AddDataProtection()
            .SetApplicationName("FrostAura.Reel")
            .PersistKeysToDbContext<ReelDbContext>();

        services.AddSingleton<ISecretProtector, SecretProtector>();

        return services;
    }
}
