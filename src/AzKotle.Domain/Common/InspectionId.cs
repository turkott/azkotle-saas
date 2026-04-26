namespace AzKotle.Domain.Common;

public readonly record struct InspectionId(Guid Value) : IComparable<InspectionId>
{
    public static InspectionId New() => new(Guid.NewGuid());

    public static InspectionId Empty => new(Guid.Empty);

    public int CompareTo(InspectionId other) => Value.CompareTo(other.Value);

    public static bool operator <(InspectionId a, InspectionId b) => a.CompareTo(b) < 0;
    public static bool operator >(InspectionId a, InspectionId b) => a.CompareTo(b) > 0;
    public static bool operator <=(InspectionId a, InspectionId b) => a.CompareTo(b) <= 0;
    public static bool operator >=(InspectionId a, InspectionId b) => a.CompareTo(b) >= 0;

    public override string ToString() => Value.ToString();
}
