using AzKotle.Domain.Common;

namespace AzKotle.Domain.Entities.Locations;

public sealed record LocationCreated(
    LocationId LocationId,
    TenantId TenantId,
    CustomerId CustomerId,
    DateTime OccurredAt) : IDomainEvent;
