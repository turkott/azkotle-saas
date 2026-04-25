using AzKotle.Domain.Common;

namespace AzKotle.Domain.Entities.Customers;

public sealed record CustomerCreated(
    CustomerId CustomerId,
    TenantId TenantId,
    CustomerType Type,
    string Name,
    DateTime OccurredAt) : IDomainEvent;
