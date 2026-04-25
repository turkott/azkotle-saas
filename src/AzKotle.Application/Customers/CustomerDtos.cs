using AzKotle.Domain.Entities.Customers;

namespace AzKotle.Application.Customers;

public sealed record CreateCustomerRequest(
    CustomerType Type,
    string Name,
    string? Ico = null,
    string? Email = null,
    string? Phone = null,
    string? Notes = null);

public sealed record UpdateCustomerRequest(
    string Name,
    string? Ico = null,
    string? Email = null,
    string? Phone = null,
    string? Notes = null);

public sealed record CustomerDto(
    Guid Id,
    CustomerType Type,
    string Name,
    string? Ico,
    string? Email,
    string? Phone,
    string? Notes,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
