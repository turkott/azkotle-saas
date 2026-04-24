using AzKotle.Domain.Common;

namespace AzKotle.Domain.Entities.Tenants;

public sealed record TenantCreated(
    TenantId TenantId,
    string Slug,
    string CompanyName,
    DateTime OccurredAt) : IDomainEvent;
