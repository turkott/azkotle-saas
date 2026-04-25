using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Locations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AzKotle.Infrastructure.Persistence.Configurations;

internal sealed class LocationConfiguration : IEntityTypeConfiguration<Location>
{
    public void Configure(EntityTypeBuilder<Location> builder)
    {
        builder.ToTable("locations");

        builder.HasKey(l => l.Id);

        builder.Property(l => l.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new LocationId(value))
            .ValueGeneratedNever();

        builder.Property(l => l.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.HasIndex(l => l.TenantId);

        builder.Property(l => l.CustomerId)
            .HasColumnName("customer_id")
            .HasConversion(id => id.Value, value => new CustomerId(value))
            .IsRequired();

        builder.HasIndex(l => l.CustomerId);

        builder.Property(l => l.Street)
            .HasColumnName("street")
            .HasMaxLength(Location.StreetMaxLength)
            .IsRequired();

        builder.Property(l => l.City)
            .HasColumnName("city")
            .HasMaxLength(Location.CityMaxLength)
            .IsRequired();

        builder.Property(l => l.Zip)
            .HasColumnName("zip")
            .HasMaxLength(Location.ZipMaxLength)
            .IsRequired();

        builder.Property(l => l.GpsLatitude)
            .HasColumnName("gps_lat")
            .HasColumnType("decimal(10,7)");

        builder.Property(l => l.GpsLongitude)
            .HasColumnName("gps_lon")
            .HasColumnType("decimal(10,7)");

        builder.Ignore(l => l.Gps);

        builder.Property(l => l.Notes)
            .HasColumnName("notes")
            .HasColumnType("text");

        builder.Property(l => l.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(l => l.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz");

        builder.HasOne<AzKotle.Domain.Entities.Customers.Customer>()
            .WithMany()
            .HasForeignKey(l => l.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(l => l.DomainEvents);
    }
}
