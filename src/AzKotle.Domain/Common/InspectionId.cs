namespace AzKotle.Domain.Common;

public readonly record struct InspectionId(Guid Value)
{
    public static InspectionId New() => new(Guid.NewGuid());

    public static InspectionId Empty => new(Guid.Empty);

    public override string ToString() => Value.ToString();
}
