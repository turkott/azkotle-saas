namespace AzKotle.Domain.Common;

public readonly record struct BoilerId(Guid Value) : IComparable<BoilerId>
{
    public static BoilerId New() => new(Guid.NewGuid());

    public static BoilerId Empty => new(Guid.Empty);

    public int CompareTo(BoilerId other) => Value.CompareTo(other.Value);

    public static bool operator <(BoilerId a, BoilerId b) => a.CompareTo(b) < 0;
    public static bool operator >(BoilerId a, BoilerId b) => a.CompareTo(b) > 0;
    public static bool operator <=(BoilerId a, BoilerId b) => a.CompareTo(b) <= 0;
    public static bool operator >=(BoilerId a, BoilerId b) => a.CompareTo(b) >= 0;

    public override string ToString() => Value.ToString();
}
