namespace AzKotle.Domain.Common;

public readonly record struct CustomerId(Guid Value)
{
    public static CustomerId New() => new(Guid.NewGuid());

    public static CustomerId Empty => new(Guid.Empty);

    public override string ToString() => Value.ToString();
}
