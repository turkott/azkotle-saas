namespace AzKotle.Domain.Common;

public readonly record struct LocationId(Guid Value)
{
    public static LocationId New() => new(Guid.NewGuid());

    public static LocationId Empty => new(Guid.Empty);

    public override string ToString() => Value.ToString();
}
