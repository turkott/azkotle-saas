using AzKotle.Domain.Entities.Tenants;
using AzKotle.Domain.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace AzKotle.Infrastructure.Persistence;

public sealed class AzKotleDbContext : DbContext
{
    public AzKotleDbContext(DbContextOptions<AzKotleDbContext> options) : base(options)
    {
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AzKotleDbContext).Assembly);
    }
}
