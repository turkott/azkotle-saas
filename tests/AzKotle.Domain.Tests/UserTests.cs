using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Users;
using FluentAssertions;

namespace AzKotle.Domain.Tests;

[Trait("Category", "Unit")]
public class UserTests
{
    private static readonly DateTimeOffset _fixedNow = new(2026, 04, 24, 12, 00, 00, TimeSpan.Zero);
    private static readonly FakeTimeProvider _time = new(_fixedNow);
    private static readonly TenantId _tenantId = TenantId.New();

    [Fact]
    public void Invite_ValidInput_CreatesInactiveUser()
    {
        var user = User.Invite(_tenantId, "Petr@Example.CZ", "  Petr Türkott  ", UserRole.Admin, _time);

        user.Id.Value.Should().NotBe(Guid.Empty);
        user.TenantId.Should().Be(_tenantId);
        user.Email.Should().Be("petr@example.cz");
        user.FullName.Should().Be("Petr Türkott");
        user.Role.Should().Be(UserRole.Admin);
        user.IsActive.Should().BeFalse();
        user.CreatedAt.Should().Be(_fixedNow.UtcDateTime);
    }

    [Fact]
    public void Invite_RaisesUserInvited_WithNormalizedEmail()
    {
        var user = User.Invite(_tenantId, "Petr@Example.CZ", "Petr", UserRole.Technician, _time);

        user.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<UserInvited>()
            .Which.Email.Should().Be("petr@example.cz");
    }

    [Fact]
    public void Invite_EmptyTenantId_Throws()
    {
        var act = () => User.Invite(TenantId.Empty, "a@b.cz", "X", UserRole.Admin);

        act.Should().Throw<ArgumentException>().WithParameterName("tenantId");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("@nouser.cz")]
    [InlineData("no-domain@")]
    [InlineData("missing.dot@tld")]
    public void Invite_InvalidEmail_Throws(string email)
    {
        var act = () => User.Invite(_tenantId, email, "X", UserRole.Admin);

        act.Should().Throw<ArgumentException>().WithParameterName("email");
    }

    [Fact]
    public void Invite_EmptyFullName_Throws()
    {
        var act = () => User.Invite(_tenantId, "a@b.cz", "  ", UserRole.Admin);

        act.Should().Throw<ArgumentException>().WithParameterName("fullName");
    }

    [Fact]
    public void Activate_InactiveUser_RaisesUserActivated()
    {
        var user = User.Invite(_tenantId, "a@b.cz", "X", UserRole.Admin, _time);
        user.ClearDomainEvents();

        user.Activate(_time);

        user.IsActive.Should().BeTrue();
        user.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<UserActivated>();
    }

    [Fact]
    public void Activate_AlreadyActive_NoOpNoEvent()
    {
        var user = User.Invite(_tenantId, "a@b.cz", "X", UserRole.Admin, _time);
        user.Activate(_time);
        user.ClearDomainEvents();

        user.Activate(_time);

        user.IsActive.Should().BeTrue();
        user.DomainEvents.Should().BeEmpty();
    }

    [Fact]
    public void Deactivate_SetsInactive()
    {
        var user = User.Invite(_tenantId, "a@b.cz", "X", UserRole.Admin, _time);
        user.Activate(_time);

        user.Deactivate();

        user.IsActive.Should().BeFalse();
    }

    [Fact]
    public void SetTechnicianLicense_TrimsNonEmpty()
    {
        var user = User.Invite(_tenantId, "a@b.cz", "X", UserRole.Technician, _time);

        user.SetTechnicianLicense("  TIČR-12345  ");

        user.TechnicianLicenseNo.Should().Be("TIČR-12345");
    }

    [Fact]
    public void SetTechnicianLicense_WhitespaceTurnsToNull()
    {
        var user = User.Invite(_tenantId, "a@b.cz", "X", UserRole.Technician, _time);

        user.SetTechnicianLicense("   ");

        user.TechnicianLicenseNo.Should().BeNull();
    }

    [Fact]
    public void RecordLogin_UpdatesLastLoginAt()
    {
        var user = User.Invite(_tenantId, "a@b.cz", "X", UserRole.Admin, _time);

        user.RecordLogin(_time);

        user.LastLoginAt.Should().Be(_fixedNow.UtcDateTime);
    }
}
