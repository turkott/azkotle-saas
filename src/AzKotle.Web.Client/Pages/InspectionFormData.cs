namespace AzKotle.Web.Client.Pages;

public sealed class InspectionFormData
{
    public DateTime? PerformedAt { get; set; } = DateTime.Today;
    public DateTime? NextDueAt { get; set; } = DateTime.Today.AddYears(1);
}
