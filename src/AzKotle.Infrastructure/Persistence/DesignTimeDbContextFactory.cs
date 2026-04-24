using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AzKotle.Infrastructure.Persistence;

internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AzKotleDbContext>
{
    public AzKotleDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("AZKOTLE_DESIGN_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=azkotle;Username=postgres;Password=postgres";

        var options = new DbContextOptionsBuilder<AzKotleDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AzKotleDbContext).Assembly.FullName))
            .Options;

        return new AzKotleDbContext(options);
    }
}
