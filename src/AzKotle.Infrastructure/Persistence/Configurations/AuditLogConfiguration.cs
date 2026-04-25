using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AzKotle.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_log");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new AuditLogId(value))
            .ValueGeneratedNever();

        builder.Property(a => a.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.HasIndex(a => a.TenantId);

        builder.Property(a => a.ActorUserId)
            .HasColumnName("actor_user_id")
            .HasConversion(
                id => id.HasValue ? id.Value.Value : (Guid?)null,
                value => value.HasValue ? new UserId(value.Value) : null);

        builder.Property(a => a.Action)
            .HasColumnName("action")
            .HasMaxLength(AuditLog.ActionMaxLength)
            .IsRequired();

        builder.HasIndex(a => a.Action);

        builder.Property(a => a.TargetType)
            .HasColumnName("target_type")
            .HasMaxLength(AuditLog.TargetTypeMaxLength)
            .IsRequired();

        builder.Property(a => a.TargetId)
            .HasColumnName("target_id");

        builder.HasIndex(a => new { a.TargetType, a.TargetId });

        builder.Property(a => a.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(AuditLog.IpAddressMaxLength);

        builder.Property(a => a.UserAgent)
            .HasColumnName("user_agent")
            .HasMaxLength(AuditLog.UserAgentMaxLength);

        builder.Property(a => a.MetadataJson)
            .HasColumnName("metadata")
            .HasColumnType("jsonb");

        builder.Property(a => a.OccurredAt)
            .HasColumnName("occurred_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.HasIndex(a => a.OccurredAt);

        builder.Ignore(a => a.DomainEvents);
    }
}
