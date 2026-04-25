namespace AzKotle.Domain.Entities.Inspections;

public enum InspectionType
{
    AnnualNv191 = 1,
    Tpg704_01Service = 2,
    Emergency = 3,
}

public enum InspectionStatus
{
    Draft = 1,
    Signed = 2,
    Archived = 3,
}
