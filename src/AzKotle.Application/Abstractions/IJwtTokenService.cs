using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Users;

namespace AzKotle.Application.Abstractions;

public interface IJwtTokenService
{
    AccessToken IssueAccessToken(UserId userId, TenantId tenantId, string email, UserRole role);

    (string Token, string Hash, DateTime ExpiresAt) GenerateRefreshToken();

    string HashRefreshToken(string token);
}

public sealed record AccessToken(string Token, DateTime ExpiresAt, int ExpiresInSeconds);
