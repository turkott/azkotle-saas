using System.Text.RegularExpressions;
using AzKotle.Domain.Common;

namespace AzKotle.Domain.Entities.Tenants;

public sealed partial class Tenant : DomainEntity
{
    public const int TrialDays = 14;
    public const int SlugMaxLength = 64;
    public const int CompanyNameMaxLength = 255;

    public TenantId Id { get; private set; }
    public string Slug { get; private set; } = string.Empty;
    public string CompanyName { get; private set; } = string.Empty;
    public string? Ico { get; private set; }
    public string? Dic { get; private set; }
    public TenantPlan Plan { get; private set; }
    public int? SeatsLimit { get; private set; }
    public DateTime? TrialEndsAt { get; private set; }
    public TenantStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Tenant()
    {
        // EF Core
    }

    public static Tenant Create(string slug, string companyName, string? ico = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(slug);
        ArgumentNullException.ThrowIfNull(companyName);

        if (string.IsNullOrWhiteSpace(slug))
        {
            throw new ArgumentException("Slug nesmí být prázdný.", nameof(slug));
        }

        if (slug.Length > SlugMaxLength)
        {
            throw new ArgumentException($"Slug může mít max {SlugMaxLength} znaků.", nameof(slug));
        }

        if (!SlugRegex().IsMatch(slug))
        {
            throw new ArgumentException(
                "Slug musí obsahovat jen malá písmena, čísla a pomlčky (a-z, 0-9, -), nesmí začínat ani končit pomlčkou.",
                nameof(slug));
        }

        if (string.IsNullOrWhiteSpace(companyName))
        {
            throw new ArgumentException("Název firmy nesmí být prázdný.", nameof(companyName));
        }

        if (companyName.Length > CompanyNameMaxLength)
        {
            throw new ArgumentException($"Název firmy může mít max {CompanyNameMaxLength} znaků.", nameof(companyName));
        }

        if (ico is not null && !IcoRegex().IsMatch(ico))
        {
            throw new ArgumentException("IČO musí mít přesně 8 číslic.", nameof(ico));
        }

        var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
        var tenant = new Tenant
        {
            Id = TenantId.New(),
            Slug = slug,
            CompanyName = companyName,
            Ico = ico,
            Plan = TenantPlan.Solo,
            Status = TenantStatus.Trial,
            TrialEndsAt = now.AddDays(TrialDays),
            CreatedAt = now,
        };
        tenant.RaiseDomainEvent(new TenantCreated(tenant.Id, tenant.Slug, tenant.CompanyName, now));
        return tenant;
    }

    public void SetDic(string? dic, TimeProvider? timeProvider = null)
    {
        if (dic is not null && !DicRegex().IsMatch(dic))
        {
            throw new ArgumentException("DIČ musí být ve formátu CZ + 8-10 číslic.", nameof(dic));
        }

        Dic = dic;
        UpdatedAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    }

    public void ChangePlan(TenantPlan plan, int seatsLimit, TimeProvider? timeProvider = null)
    {
        if (seatsLimit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(seatsLimit), "Limit uživatelů musí být aspoň 1.");
        }

        Plan = plan;
        SeatsLimit = seatsLimit;
        UpdatedAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    }

    public void Activate(TimeProvider? timeProvider = null)
    {
        if (Status == TenantStatus.Churned)
        {
            throw new InvalidOperationException("Churnutý tenant nelze aktivovat.");
        }

        Status = TenantStatus.Active;
        TrialEndsAt = null;
        UpdatedAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    }

    public void Suspend(TimeProvider? timeProvider = null)
    {
        if (Status == TenantStatus.Churned)
        {
            throw new InvalidOperationException("Churnutý tenant nelze pozastavit.");
        }

        Status = TenantStatus.Suspended;
        UpdatedAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    }

    public void MarkChurned(TimeProvider? timeProvider = null)
    {
        Status = TenantStatus.Churned;
        UpdatedAt = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
    }

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$")]
    private static partial Regex SlugRegex();

    [GeneratedRegex("^[0-9]{8}$")]
    private static partial Regex IcoRegex();

    [GeneratedRegex("^CZ[0-9]{8,10}$")]
    private static partial Regex DicRegex();
}
