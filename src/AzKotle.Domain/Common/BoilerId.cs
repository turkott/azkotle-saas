namespace AzKotle.Domain.Common;

public readonly record struct BoilerId(Guid Value)
{
    public static BoilerId New() => new(Guid.NewGuid());

    public static BoilerId Empty => new(Guid.Empty);

    public override string ToString() => Value.ToString();
}
