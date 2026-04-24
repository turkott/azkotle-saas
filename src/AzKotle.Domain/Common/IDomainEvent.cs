namespace AzKotle.Domain.Common;

public interface IDomainEvent
{
    DateTime OccurredAt { get; }
}
