using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Inspections;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AzKotle.Infrastructure.Persistence.Configurations;

internal sealed class InspectionConfiguration : IEntityTypeConfiguration<Inspection>
{
    public void Configure(EntityTypeBuilder<Inspection> builder)
    {
        builder.ToTable("inspections");

        builder.HasKey(i => i.Id);

        builder.Property(i => i.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new InspectionId(value))
            .ValueGeneratedNever();

        builder.Property(i => i.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.HasIndex(i => i.TenantId);

        builder.Property(i => i.BoilerId)
            .HasColumnName("boiler_id")
            .HasConversion(id => id.Value, value => new BoilerId(value))
            .IsRequired();

        builder.HasIndex(i => i.BoilerId);

        builder.Property(i => i.TechnicianId)
            .HasColumnName("technician_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.Property(i => i.Type)
            .HasColumnName("inspection_type")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(i => i.PerformedAt)
            .HasColumnName("performed_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(i => i.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(i => i.FormDataJson)
            .HasColumnName("form_data")
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(i => i.Findings)
            .HasColumnName("findings")
            .HasColumnType("text");

        builder.Property(i => i.Recommendations)
            .HasColumnName("recommendations")
            .HasColumnType("text");

        builder.Property(i => i.NextDueAt)
            .HasColumnName("next_due_at")
            .HasColumnType("date");

        builder.Property(i => i.PdfB2Key)
            .HasColumnName("pdf_b2_key")
            .HasMaxLength(Inspection.PdfB2KeyMaxLength);

        builder.Property(i => i.PdfSha256)
            .HasColumnName("pdf_sha256")
            .HasMaxLength(Inspection.PdfSha256Length);

        builder.Property(i => i.SignedAt)
            .HasColumnName("signed_at")
            .HasColumnType("timestamptz");

        builder.Property(i => i.SignatureData)
            .HasColumnName("signature_data")
            .HasColumnType("bytea");

        builder.Property(i => i.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(i => i.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz");

        builder.HasOne<AzKotle.Domain.Entities.Boilers.Boiler>()
            .WithMany()
            .HasForeignKey(i => i.BoilerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<AzKotle.Domain.Entities.Users.User>()
            .WithMany()
            .HasForeignKey(i => i.TechnicianId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(i => i.DomainEvents);
    }
}
