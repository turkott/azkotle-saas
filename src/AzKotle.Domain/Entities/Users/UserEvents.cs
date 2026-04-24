using AzKotle.Domain.Common;

namespace AzKotle.Domain.Entities.Users;

public sealed record UserInvited(
    UserId UserId,
    TenantId TenantId,
    string Email,
    UserRole Role,
    DateTime OccurredAt) : IDomainEvent;

public sealed record UserActivated(
    UserId UserId,
    TenantId TenantId,
    DateTime OccurredAt) : IDomainEvent;
