using AzKotle.Domain.Entities.Customers;

namespace AzKotle.Web.Client.Pages;

public sealed class CustomerFormData
{
    public CustomerType Type { get; set; } = CustomerType.Company;
    public string Name { get; set; } = string.Empty;
    public string? Ico { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Notes { get; set; }
}
