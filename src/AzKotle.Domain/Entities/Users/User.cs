using System.Text.RegularExpressions;
using AzKotle.Domain.Common;

namespace AzKotle.Domain.Entities.Users;

public sealed partial class User : DomainEntity
{
    public const int EmailMaxLength = 255;
    public const int FullNameMaxLength = 255;

    public UserId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string FullName { get; private set; } = string.Empty;
    public UserRole Role { get; private set; }
    public string? TechnicianLicenseNo { get; private set; }
    public string? Phone { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime? LastLoginAt { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string? PasswordHash { get; private set; }

    private User()
    {
        // EF Core
    }

    public static User Invite(TenantId tenantId, string email, string fullName, UserRole role, TimeProvider? timeProvider = null)
    {
        if (tenantId == TenantId.Empty)
        {
            throw new ArgumentException("Tenant musí být vyplněn.", nameof(tenantId));
        }

        ArgumentNullException.ThrowIfNull(email);
        ArgumentNullException.ThrowIfNull(fullName);

        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email nesmí být prázdný.", nameof(email));
        }

        if (email.Length > EmailMaxLength)
        {
            throw new ArgumentException($"Email může mít max {EmailMaxLength} znaků.", nameof(email));
        }

        if (!EmailRegex().IsMatch(email))
        {
            throw new ArgumentException("Email nemá platný formát.", nameof(email));
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new ArgumentException("Jméno nesmí být prázdné.", nameof(fullName));
        }

        if (fullName.Length > FullNameMaxLength)
        {
            throw new ArgumentException($"Jméno může mít max {FullNameMaxLength} znaků.", nameof(fullName));
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = new User
        {
            Id = UserId.New(),
            TenantId = tenantId,
            Email = normalizedEmail,
            FullName = fullName.Trim(),
            Role = role,
            IsActive = false,
            CreatedAt = now,
        };
        user.RaiseDomainEvent(new UserInvited(user.Id, tenantId, normalizedEmail, role, now));
        return user;
    }

    public static User RegisterOwner(
        TenantId tenantId,
        string email,
        string fullName,
        string passwordHash,
        TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);

        var user = Invite(tenantId, email, fullName, UserRole.Owner, timeProvider);
        user.PasswordHash = passwordHash;
        user.IsActive = true;
        user.RaiseDomainEvent(new UserActivated(user.Id, user.TenantId,
            (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime));
        return user;
    }

    public void SetPassword(string passwordHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        PasswordHash = passwordHash;
    }

    public void Activate(TimeProvider? timeProvider = null)
    {
        if (IsActive)
        {
            return;
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        IsActive = true;
        RaiseDomainEvent(new UserActivated(Id, TenantId, now));
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    public void SetTechnicianLicense(string? licenseNo)
    {
        TechnicianLicenseNo = string.IsNullOrWhiteSpace(licenseNo) ? null : licenseNo.Trim();
    }

    public void SetPhone(string? phone)
    {
        Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
    }

    public void RecordLogin(TimeProvider? timeProvider = null)
    {
        LastLoginAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    }

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();
}
