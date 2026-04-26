namespace AzKotle.Domain.Common;

public readonly record struct LocationId(Guid Value) : IComparable<LocationId>
{
    public static LocationId New() => new(Guid.NewGuid());

    public static LocationId Empty => new(Guid.Empty);

    public int CompareTo(LocationId other) => Value.CompareTo(other.Value);

    public static bool operator <(LocationId a, LocationId b) => a.CompareTo(b) < 0;
    public static bool operator >(LocationId a, LocationId b) => a.CompareTo(b) > 0;
    public static bool operator <=(LocationId a, LocationId b) => a.CompareTo(b) <= 0;
    public static bool operator >=(LocationId a, LocationId b) => a.CompareTo(b) >= 0;

    public override string ToString() => Value.ToString();
}
