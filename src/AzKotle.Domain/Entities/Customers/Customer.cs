using System.Text.RegularExpressions;
using AzKotle.Domain.Common;

namespace AzKotle.Domain.Entities.Customers;

public sealed partial class Customer : DomainEntity
{
    public const int NameMaxLength = 255;
    public const int EmailMaxLength = 255;
    public const int PhoneMaxLength = 32;

    public CustomerId Id { get; private set; }
    public TenantId TenantId { get; private set; }
    public CustomerType Type { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? Ico { get; private set; }
    public string? Email { get; private set; }
    public string? Phone { get; private set; }
    public string? Notes { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Customer()
    {
        // EF Core
    }

    public static Customer Create(
        TenantId tenantId,
        CustomerType type,
        string name,
        TimeProvider? timeProvider = null)
    {
        if (tenantId == TenantId.Empty)
        {
            throw new ArgumentException("Tenant musí být vyplněn.", nameof(tenantId));
        }

        ArgumentNullException.ThrowIfNull(name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Jméno zákazníka nesmí být prázdné.", nameof(name));
        }

        if (name.Length > NameMaxLength)
        {
            throw new ArgumentException($"Jméno zákazníka může mít max {NameMaxLength} znaků.", nameof(name));
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var customer = new Customer
        {
            Id = CustomerId.New(),
            TenantId = tenantId,
            Type = type,
            Name = name.Trim(),
            CreatedAt = now,
        };
        customer.RaiseDomainEvent(new CustomerCreated(customer.Id, tenantId, type, customer.Name, now));
        return customer;
    }

    public void SetIco(string? ico, TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(ico))
        {
            Ico = null;
        }
        else
        {
            if (!IcoRegex().IsMatch(ico))
            {
                throw new ArgumentException("IČO musí mít přesně 8 číslic.", nameof(ico));
            }

            if (Type != CustomerType.Company)
            {
                throw new InvalidOperationException("IČO lze nastavit pouze pro firmu.");
            }

            Ico = ico;
        }

        Touch(timeProvider);
    }

    public void SetContactInfo(string? email, string? phone, TimeProvider? timeProvider = null)
    {
        if (!string.IsNullOrWhiteSpace(email))
        {
            var trimmed = email.Trim();
            if (trimmed.Length > EmailMaxLength)
            {
                throw new ArgumentException($"Email může mít max {EmailMaxLength} znaků.", nameof(email));
            }

            if (!EmailRegex().IsMatch(trimmed))
            {
                throw new ArgumentException("Email nemá platný formát.", nameof(email));
            }

            Email = trimmed.ToLowerInvariant();
        }
        else
        {
            Email = null;
        }

        if (!string.IsNullOrWhiteSpace(phone))
        {
            if (phone.Length > PhoneMaxLength)
            {
                throw new ArgumentException($"Telefon může mít max {PhoneMaxLength} znaků.", nameof(phone));
            }

            Phone = phone.Trim();
        }
        else
        {
            Phone = null;
        }

        Touch(timeProvider);
    }

    public void Rename(string name, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Jméno zákazníka nesmí být prázdné.", nameof(name));
        }

        if (name.Length > NameMaxLength)
        {
            throw new ArgumentException($"Jméno zákazníka může mít max {NameMaxLength} znaků.", nameof(name));
        }

        Name = name.Trim();
        Touch(timeProvider);
    }

    public void SetNotes(string? notes, TimeProvider? timeProvider = null)
    {
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Touch(timeProvider);
    }

    private void Touch(TimeProvider? timeProvider) =>
        UpdatedAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;

    [GeneratedRegex("^[0-9]{8}$")]
    private static partial Regex IcoRegex();

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex EmailRegex();
}
