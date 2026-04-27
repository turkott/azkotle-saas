using AzKotle.Domain.Common;

namespace AzKotle.Domain.Entities.Auth;

public sealed class PasswordResetToken : DomainEntity
{
    public const int TokenHashLength = 64;

    public PasswordResetTokenId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public UserId UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime ExpiresAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UsedAt { get; private set; }

    private PasswordResetToken()
    {
        // EF Core
    }

    public static PasswordResetToken Issue(
        TenantId tenantId,
        UserId userId,
        string tokenHash,
        DateTime expiresAt,
        TimeProvider? timeProvider = null)
    {
        if (tenantId == TenantId.Empty)
        {
            throw new ArgumentException("Tenant musí být vyplněn.", nameof(tenantId));
        }

        if (userId == UserId.Empty)
        {
            throw new ArgumentException("User musí být vyplněn.", nameof(userId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        if (tokenHash.Length != TokenHashLength)
        {
            throw new ArgumentException(
                $"Token hash musí mít přesně {TokenHashLength} znaků (SHA-256 hex).", nameof(tokenHash));
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        if (expiresAt <= now)
        {
            throw new ArgumentException("Reset token musí expirovat v budoucnu.", nameof(expiresAt));
        }

        return new PasswordResetToken
        {
            Id = PasswordResetTokenId.New(),
            TenantId = tenantId,
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedAt = now,
        };
    }

    public bool IsRedeemable(DateTime utcNow) =>
        UsedAt is null && ExpiresAt > utcNow;

    public void MarkUsed(TimeProvider? timeProvider = null)
    {
        if (UsedAt is not null)
        {
            throw new InvalidOperationException("Token už byl použit.");
        }

        UsedAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    }
}
