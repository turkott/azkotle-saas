using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AzKotle.Infrastructure.Persistence.Configurations;

internal sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .ValueGeneratedNever();

        builder.Property(t => t.Slug)
            .HasColumnName("slug")
            .HasMaxLength(Tenant.SlugMaxLength)
            .IsRequired();

        builder.HasIndex(t => t.Slug).IsUnique();

        builder.Property(t => t.CompanyName)
            .HasColumnName("company_name")
            .HasMaxLength(Tenant.CompanyNameMaxLength)
            .IsRequired();

        builder.Property(t => t.Ico)
            .HasColumnName("ico")
            .HasMaxLength(8);

        builder.Property(t => t.Dic)
            .HasColumnName("dic")
            .HasMaxLength(16);

        builder.Property(t => t.Plan)
            .HasColumnName("plan")
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(t => t.SeatsLimit)
            .HasColumnName("seats_limit");

        builder.Property(t => t.TrialEndsAt)
            .HasColumnName("trial_ends_at")
            .HasColumnType("timestamptz");

        builder.Property(t => t.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz");

        builder.Ignore(t => t.DomainEvents);
    }
}
