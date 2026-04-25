using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Boilers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AzKotle.Infrastructure.Persistence.Configurations;

internal sealed class BoilerConfiguration : IEntityTypeConfiguration<Boiler>
{
    public void Configure(EntityTypeBuilder<Boiler> builder)
    {
        builder.ToTable("boilers");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new BoilerId(value))
            .ValueGeneratedNever();

        builder.Property(b => b.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.HasIndex(b => b.TenantId);

        builder.Property(b => b.LocationId)
            .HasColumnName("location_id")
            .HasConversion(id => id.Value, value => new LocationId(value))
            .IsRequired();

        builder.HasIndex(b => b.LocationId);

        builder.Property(b => b.QrCode)
            .HasColumnName("qr_code")
            .HasMaxLength(Boiler.QrCodeMaxLength)
            .IsRequired();

        builder.HasIndex(b => b.QrCode).IsUnique();

        builder.Property(b => b.Manufacturer)
            .HasColumnName("manufacturer")
            .HasMaxLength(Boiler.ManufacturerMaxLength)
            .IsRequired();

        builder.Property(b => b.Model)
            .HasColumnName("model")
            .HasMaxLength(Boiler.ModelMaxLength)
            .IsRequired();

        builder.Property(b => b.SerialNo)
            .HasColumnName("serial_no")
            .HasMaxLength(Boiler.SerialNoMaxLength)
            .IsRequired();

        builder.Property(b => b.OutputKw)
            .HasColumnName("output_kw")
            .HasColumnType("decimal(5,1)")
            .IsRequired();

        builder.Property(b => b.FuelType)
            .HasColumnName("fuel_type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(b => b.InstalledAt)
            .HasColumnName("installed_at")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(b => b.LastInspectionAt)
            .HasColumnName("last_inspection_at")
            .HasColumnType("date");

        builder.Property(b => b.NextInspectionDue)
            .HasColumnName("next_inspection_due")
            .HasColumnType("date");

        builder.Property(b => b.Notes)
            .HasColumnName("notes")
            .HasColumnType("text");

        builder.Property(b => b.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(b => b.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz");

        builder.HasOne<AzKotle.Domain.Entities.Locations.Location>()
            .WithMany()
            .HasForeignKey(b => b.LocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(b => b.DomainEvents);
    }
}
