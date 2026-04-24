using AzKotle.Domain.Entities.Tenants;
using FluentAssertions;

namespace AzKotle.Domain.Tests;

[Trait("Category", "Unit")]
public class TenantTests
{
    private static readonly DateTimeOffset _fixedNow = new(2026, 04, 24, 12, 00, 00, TimeSpan.Zero);
    private static readonly FakeTimeProvider _time = new(_fixedNow);

    [Fact]
    public void Create_ValidInput_InitializesDefaults()
    {
        var tenant = Tenant.Create("acme", "ACME s.r.o.", "12345678", _time);

        tenant.Id.Value.Should().NotBe(Guid.Empty);
        tenant.Slug.Should().Be("acme");
        tenant.CompanyName.Should().Be("ACME s.r.o.");
        tenant.Ico.Should().Be("12345678");
        tenant.Plan.Should().Be(TenantPlan.Solo);
        tenant.Status.Should().Be(TenantStatus.Trial);
        tenant.TrialEndsAt.Should().Be(_fixedNow.UtcDateTime.AddDays(Tenant.TrialDays));
        tenant.CreatedAt.Should().Be(_fixedNow.UtcDateTime);
        tenant.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Create_RaisesTenantCreated_Event()
    {
        var tenant = Tenant.Create("acme", "ACME s.r.o.", null, _time);

        tenant.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<TenantCreated>()
            .Which.Should().Match<TenantCreated>(e =>
                e.TenantId == tenant.Id &&
                e.Slug == "acme" &&
                e.CompanyName == "ACME s.r.o." &&
                e.OccurredAt == _fixedNow.UtcDateTime);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Acme")]        // uppercase forbidden
    [InlineData("-leading")]    // leading hyphen
    [InlineData("trailing-")]   // trailing hyphen
    [InlineData("has space")]   // space
    [InlineData("has_underscore")]
    public void Create_InvalidSlug_Throws(string slug)
    {
        var act = () => Tenant.Create(slug, "ACME");

        act.Should().Throw<ArgumentException>().WithParameterName("slug");
    }

    [Fact]
    public void Create_SlugTooLong_Throws()
    {
        var slug = new string('a', Tenant.SlugMaxLength + 1);

        var act = () => Tenant.Create(slug, "ACME");

        act.Should().Throw<ArgumentException>().WithParameterName("slug");
    }

    [Fact]
    public void Create_EmptyCompanyName_Throws()
    {
        var act = () => Tenant.Create("acme", "   ");

        act.Should().Throw<ArgumentException>().WithParameterName("companyName");
    }

    [Fact]
    public void Create_CompanyNameTooLong_Throws()
    {
        var name = new string('x', Tenant.CompanyNameMaxLength + 1);

        var act = () => Tenant.Create("acme", name);

        act.Should().Throw<ArgumentException>().WithParameterName("companyName");
    }

    [Theory]
    [InlineData("1234567")]   // 7 digits
    [InlineData("123456789")] // 9 digits
    [InlineData("1234567a")]  // not all digits
    public void Create_InvalidIco_Throws(string ico)
    {
        var act = () => Tenant.Create("acme", "ACME", ico);

        act.Should().Throw<ArgumentException>().WithParameterName("ico");
    }

    [Fact]
    public void SetDic_ValidFormat_Updates()
    {
        var tenant = Tenant.Create("acme", "ACME", null, _time);

        tenant.SetDic("CZ12345678", _time);

        tenant.Dic.Should().Be("CZ12345678");
        tenant.UpdatedAt.Should().Be(_fixedNow.UtcDateTime);
    }

    [Theory]
    [InlineData("12345678")]    // no CZ prefix
    [InlineData("CZ1234567")]   // 7 digits
    [InlineData("CZ12345678901")] // 11 digits
    public void SetDic_InvalidFormat_Throws(string dic)
    {
        var tenant = Tenant.Create("acme", "ACME", null, _time);

        var act = () => tenant.SetDic(dic);

        act.Should().Throw<ArgumentException>().WithParameterName("dic");
    }

    [Fact]
    public void ChangePlan_SetsPlanAndSeatsLimit()
    {
        var tenant = Tenant.Create("acme", "ACME", null, _time);

        tenant.ChangePlan(TenantPlan.Pro, 5, _time);

        tenant.Plan.Should().Be(TenantPlan.Pro);
        tenant.SeatsLimit.Should().Be(5);
    }

    [Fact]
    public void ChangePlan_ZeroSeatsLimit_Throws()
    {
        var tenant = Tenant.Create("acme", "ACME", null, _time);

        var act = () => tenant.ChangePlan(TenantPlan.Pro, 0);

        act.Should().Throw<ArgumentOutOfRangeException>().WithParameterName("seatsLimit");
    }

    [Fact]
    public void Activate_FromTrial_BecomesActiveAndClearsTrialEnd()
    {
        var tenant = Tenant.Create("acme", "ACME", null, _time);

        tenant.Activate(_time);

        tenant.Status.Should().Be(TenantStatus.Active);
        tenant.TrialEndsAt.Should().BeNull();
    }

    [Fact]
    public void Activate_FromChurned_Throws()
    {
        var tenant = Tenant.Create("acme", "ACME", null, _time);
        tenant.MarkChurned(_time);

        var act = () => tenant.Activate();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Suspend_FromChurned_Throws()
    {
        var tenant = Tenant.Create("acme", "ACME", null, _time);
        tenant.MarkChurned(_time);

        var act = () => tenant.Suspend();

        act.Should().Throw<InvalidOperationException>();
    }
}
