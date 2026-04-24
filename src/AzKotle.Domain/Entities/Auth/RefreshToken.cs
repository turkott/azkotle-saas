using AzKotle.Domain.Common;

namespace AzKotle.Domain.Entities.Auth;

public sealed class RefreshToken : DomainEntity
{
    public const int TokenHashLength = 64;

    public RefreshTokenId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public UserId UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime ExpiresAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? RevokedAt { get; private set; }
    public RefreshTokenId? ReplacedById { get; private set; }

    private RefreshToken()
    {
        // EF Core
    }

    public static RefreshToken Issue(
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
            throw new ArgumentException("Refresh token musí expirovat v budoucnu.", nameof(expiresAt));
        }

        return new RefreshToken
        {
            Id = RefreshTokenId.New(),
            TenantId = tenantId,
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = expiresAt,
            CreatedAt = now,
        };
    }

    public bool IsActive(DateTime utcNow) =>
        RevokedAt is null && ExpiresAt > utcNow;

    public void RevokeAndReplace(RefreshTokenId replacedById, TimeProvider? timeProvider = null)
    {
        if (RevokedAt is not null)
        {
            throw new InvalidOperationException("Token je už revokovaný.");
        }

        RevokedAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        ReplacedById = replacedById;
    }

    public void Revoke(TimeProvider? timeProvider = null)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    }
}
