namespace AzKotle.Application.Tenants.Team;

public sealed record CreateTechnicianRequest(
    string FullName,
    string Email,
    string Password);

public sealed record TeamUserDto(
    Guid Id,
    string Email,
    string FullName,
    string Role,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastLoginAt);
