using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Auth;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AzKotle.Infrastructure.Persistence.Configurations;

internal sealed class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_tokens");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasConversion(id => id.Value, value => new PasswordResetTokenId(value))
            .ValueGeneratedNever();

        builder.Property(t => t.TenantId)
            .HasColumnName("tenant_id")
            .HasConversion(id => id.Value, value => new TenantId(value))
            .IsRequired();

        builder.HasIndex(t => t.TenantId);

        builder.Property(t => t.UserId)
            .HasColumnName("user_id")
            .HasConversion(id => id.Value, value => new UserId(value))
            .IsRequired();

        builder.HasIndex(t => t.UserId);

        builder.Property(t => t.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(PasswordResetToken.TokenHashLength)
            .IsRequired();

        builder.HasIndex(t => t.TokenHash).IsUnique();

        builder.Property(t => t.ExpiresAt)
            .HasColumnName("expires_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("timestamptz")
            .IsRequired();

        builder.Property(t => t.UsedAt)
            .HasColumnName("used_at")
            .HasColumnType("timestamptz");

        builder.HasOne<AzKotle.Domain.Entities.Users.User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(t => t.DomainEvents);
    }
}
