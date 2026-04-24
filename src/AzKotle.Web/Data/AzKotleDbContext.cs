using AzKotle.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace AzKotle.Web.Data;

public class AzKotleDbContext(DbContextOptions<AzKotleDbContext> options) : DbContext(options)
{
    public DbSet<Kotel> Kotle => Set<Kotel>();
    public DbSet<ServisniZprava> ServisniZpravy => Set<ServisniZprava>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        base.OnModelCreating(b);

        b.Entity<Kotel>(e =>
        {
            e.HasQueryFilter(k => k.DeletedAt == null);
            e.HasIndex(k => k.TenantId);
            e.HasIndex(k => new { k.TenantId, k.VyrobniCislo });
            e.Property(k => k.Vyrobce).HasMaxLength(100);
            e.Property(k => k.Model).HasMaxLength(100);
            e.Property(k => k.VyrobniCislo).HasMaxLength(100);
            e.Property(k => k.VlastnikJmeno).HasMaxLength(200);
            e.Property(k => k.VlastnikTelefon).HasMaxLength(50);
            e.Property(k => k.VlastnikEmail).HasMaxLength(200);
            e.Property(k => k.Umisteni).HasMaxLength(500);
            e.Property(k => k.VykonKw).HasPrecision(6, 2);
        });

        b.Entity<ServisniZprava>(e =>
        {
            e.HasQueryFilter(z => z.DeletedAt == null);
            e.HasIndex(z => z.TenantId);
            e.HasIndex(z => new { z.TenantId, z.KotelId, z.DatumZasahu });
            e.HasOne(z => z.Kotel).WithMany(k => k.ServisniZpravy).HasForeignKey(z => z.KotelId).OnDelete(DeleteBehavior.Restrict);
            e.Property(z => z.Technik).HasMaxLength(200);
        });
    }

    public override int SaveChanges()
    {
        ApplyAuditFields();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplyAuditFields();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplyAuditFields()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<BaseEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Property(nameof(BaseEntity.CreatedAt)).IsModified = false;
            }
        }
    }
}
