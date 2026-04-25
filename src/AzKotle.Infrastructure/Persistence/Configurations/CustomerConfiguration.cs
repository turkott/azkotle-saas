using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AzKotle.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new CustomerId(value))
            .ValueGeneratedNever();

        builder.Property(c => c.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.HasIndex(c => c.TenantId);

        builder.Property(c => c.Type)
            .HasColumnName("type")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(c => c.Name)
            .HasColumnName("name")
            .HasMaxLength(Customer.NameMaxLength)
            .IsRequired();

        builder.Property(c => c.Ico)
            .HasColumnName("ico")
            .HasMaxLength(8);

        builder.Property(c => c.Email)
            .HasColumnName("email")
            .HasMaxLength(Customer.EmailMaxLength);

        builder.Property(c => c.Phone)
            .HasColumnName("phone")
            .HasMaxLength(Customer.PhoneMaxLength);

        builder.Property(c => c.Notes)
            .HasColumnName("notes")
            .HasColumnType("text");

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(c => c.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("timestamptz");

        builder.HasOne<AzKotle.Domain.Entities.Tenants.Tenant>()
            .WithMany()
            .HasForeignKey(c => c.TenantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(c => c.DomainEvents);
    }
}
