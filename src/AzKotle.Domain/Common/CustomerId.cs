namespace AzKotle.Domain.Common;

public readonly record struct CustomerId(Guid Value) : IComparable<CustomerId>
{
    public static CustomerId New() => new(Guid.NewGuid());

    public static CustomerId Empty => new(Guid.Empty);

    public int CompareTo(CustomerId other) => Value.CompareTo(other.Value);

    public static bool operator <(CustomerId a, CustomerId b) => a.CompareTo(b) < 0;
    public static bool operator >(CustomerId a, CustomerId b) => a.CompareTo(b) > 0;
    public static bool operator <=(CustomerId a, CustomerId b) => a.CompareTo(b) <= 0;
    public static bool operator >=(CustomerId a, CustomerId b) => a.CompareTo(b) >= 0;

    public override string ToString() => Value.ToString();
}
