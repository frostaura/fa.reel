using FrostAura.Reel.Application.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FrostAura.Reel.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for `dotnet ef` — migrations never need the full app DI graph.
/// Connection string comes from REEL_DESIGN_CONNECTION / ConnectionStrings__Postgres when
/// present; schema-only operations (migrations add) work with the placeholder.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ReelDbContext>
{
    public ReelDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("REEL_DESIGN_CONNECTION")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=reel;Username=reel;Password=reel";

        var options = new DbContextOptionsBuilder<ReelDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseVector())
            .Options;

        return new ReelDbContext(options, new AccountContext());
    }
}
