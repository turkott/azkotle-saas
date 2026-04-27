using System.Text.Json;
using AzKotle.Application.Abstractions;
using AzKotle.Application.Tenants.Team;
using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Audit;
using AzKotle.Domain.Entities.Users;
using AzKotle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AzKotle.Infrastructure.Tenants;

public sealed class TeamService
{
    private const string UniqueViolationSqlState = "23505";

    private static readonly JsonSerializerOptions _metadataJson = new(JsonSerializerDefaults.Web);

    private readonly AzKotleDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly TimeProvider _time;

    public TeamService(AzKotleDbContext db, IPasswordHasher hasher, TimeProvider time)
    {
        _db = db;
        _hasher = hasher;
        _time = time;
    }

    public async Task<IReadOnlyList<TeamUserDto>> ListAsync(TenantId tenantId, CancellationToken ct)
    {
        // Explicit TenantId filter is defense-in-depth on top of Postgres RLS — the
        // RLS policy already restricts visibility, but the WHERE makes the contract
        // obvious in code review and survives any future RLS misconfiguration.
        var users = await _db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId)
            .OrderBy(u => u.FullName)
            .ToListAsync(ct);

        return users.Select(ToDto).ToList();
    }

    public async Task<CreateTechnicianResult> CreateTechnicianAsync(
        TenantId tenantId,
        UserId actorId,
        CreateTechnicianRequest request,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct)
    {
        var passwordHash = _hasher.Hash(request.Password);
        var user = User.Invite(tenantId, request.Email, request.FullName, UserRole.Technician, _time);
        user.SetPassword(passwordHash);
        user.Activate(_time);
        _db.Users.Add(user);

        var metadata = JsonSerializer.Serialize(new
        {
            user_id = user.Id.Value,
            email = user.Email,
            full_name = user.FullName,
            role = user.Role.ToString(),
        }, _metadataJson);

        _db.AuditLog.Add(AuditLog.Record(
            tenantId,
            actorId,
            action: "user.created",
            targetType: "user",
            targetId: user.Id.Value,
            ipAddress,
            userAgent,
            metadata,
            _time));

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return new CreateTechnicianResult.EmailTaken();
        }

        return new CreateTechnicianResult.Success(ToDto(user));
    }

    public async Task<DeleteUserResult> DeleteAsync(
        TenantId tenantId,
        UserId actorId,
        UserId targetId,
        string? ipAddress,
        string? userAgent,
        CancellationToken ct)
    {
        if (actorId == targetId)
        {
            return new DeleteUserResult.SelfDeleteForbidden();
        }

        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Id == targetId && u.TenantId == tenantId, ct);
        if (user is null)
        {
            return new DeleteUserResult.NotFound();
        }

        // Soft-delete: Inspection.TechnicianId má FK Restrict, hard delete by selhal
        // pro každého technika s revizí. Deactivate vyřadí uživatele z přihlašování,
        // historická data zůstanou intaktní.
        user.Deactivate();

        // Aktivní refresh tokeny okamžitě revokujeme — bez toho by deaktivovaný
        // uživatel mohl ještě po dobu access token TTL volat API přes refresh flow.
        // Login už projde přes IsActive check, ale refresh by token získal nový.
        var now = _time.GetUtcNow().UtcDateTime;
        var activeTokens = await _db.RefreshTokens
            .Where(t => t.UserId == targetId && t.RevokedAt == null && t.ExpiresAt > now)
            .ToListAsync(ct);
        foreach (var token in activeTokens)
        {
            token.Revoke(_time);
        }

        var metadata = JsonSerializer.Serialize(new
        {
            user_id = user.Id.Value,
            email = user.Email,
            role = user.Role.ToString(),
            revoked_refresh_tokens = activeTokens.Count,
        }, _metadataJson);

        _db.AuditLog.Add(AuditLog.Record(
            tenantId,
            actorId,
            action: "user.deleted",
            targetType: "user",
            targetId: user.Id.Value,
            ipAddress,
            userAgent,
            metadata,
            _time));

        await _db.SaveChangesAsync(ct);
        return new DeleteUserResult.Success();
    }

    private static TeamUserDto ToDto(User u) => new(
        u.Id.Value,
        u.Email,
        u.FullName,
        u.Role.ToString(),
        u.IsActive,
        u.CreatedAt,
        u.LastLoginAt);

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == UniqueViolationSqlState;
}

public abstract record CreateTechnicianResult
{
    public sealed record Success(TeamUserDto User) : CreateTechnicianResult;
    public sealed record EmailTaken : CreateTechnicianResult;
}

public abstract record DeleteUserResult
{
    public sealed record Success : DeleteUserResult;
    public sealed record NotFound : DeleteUserResult;
    public sealed record SelfDeleteForbidden : DeleteUserResult;
}
