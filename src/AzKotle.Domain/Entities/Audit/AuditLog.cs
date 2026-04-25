using AzKotle.Domain.Common;

namespace AzKotle.Domain.Entities.Audit;

public sealed class AuditLog : DomainEntity
{
    public const int ActionMaxLength = 64;
    public const int TargetTypeMaxLength = 64;
    public const int IpAddressMaxLength = 64;
    public const int UserAgentMaxLength = 512;

    public AuditLogId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public UserId? ActorUserId { get; private set; }
    public string Action { get; private set; } = string.Empty;
    public string TargetType { get; private set; } = string.Empty;
    public Guid? TargetId { get; private set; }
    public string? IpAddress { get; private set; }
    public string? UserAgent { get; private set; }
    public string? MetadataJson { get; private set; }
    public DateTime OccurredAt { get; private set; }

    private AuditLog()
    {
        // EF Core
    }

    public static AuditLog Record(
        TenantId tenantId,
        UserId? actorUserId,
        string action,
        string targetType,
        Guid? targetId,
        string? ipAddress,
        string? userAgent,
        string? metadataJson,
        TimeProvider? timeProvider = null)
    {
        if (tenantId == TenantId.Empty)
        {
            throw new ArgumentException("Tenant musí být vyplněn.", nameof(tenantId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetType);

        if (action.Length > ActionMaxLength)
        {
            throw new ArgumentException($"Action může mít max {ActionMaxLength} znaků.", nameof(action));
        }
        if (targetType.Length > TargetTypeMaxLength)
        {
            throw new ArgumentException($"TargetType může mít max {TargetTypeMaxLength} znaků.", nameof(targetType));
        }
        if (ipAddress is { Length: > IpAddressMaxLength })
        {
            throw new ArgumentException($"IP adresa může mít max {IpAddressMaxLength} znaků.", nameof(ipAddress));
        }
        if (userAgent is { Length: > UserAgentMaxLength })
        {
            userAgent = userAgent[..UserAgentMaxLength];
        }

        return new AuditLog
        {
            Id = AuditLogId.New(),
            TenantId = tenantId,
            ActorUserId = actorUserId is { } a && a != UserId.Empty ? a : null,
            Action = action,
            TargetType = targetType,
            TargetId = targetId,
            IpAddress = string.IsNullOrWhiteSpace(ipAddress) ? null : ipAddress,
            UserAgent = string.IsNullOrWhiteSpace(userAgent) ? null : userAgent,
            MetadataJson = string.IsNullOrWhiteSpace(metadataJson) ? null : metadataJson,
            OccurredAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime,
        };
    }
}
