using AzKotle.Domain.Entities.Auth;
using AzKotle.Domain.Entities.Boilers;
using AzKotle.Domain.Entities.Customers;
using AzKotle.Domain.Entities.Locations;
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

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<Location> Locations => Set<Location>();

    public DbSet<Boiler> Boilers => Set<Boiler>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("public");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AzKotleDbContext).Assembly);
    }
}
