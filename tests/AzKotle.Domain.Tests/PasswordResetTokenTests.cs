using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Auth;
using FluentAssertions;

namespace AzKotle.Domain.Tests;

[Trait("Category", "Unit")]
public class PasswordResetTokenTests
{
    private static readonly DateTimeOffset _fixedNow = new(2026, 04, 27, 12, 00, 00, TimeSpan.Zero);
    private static readonly FakeTimeProvider _time = new(_fixedNow);

    private static readonly TenantId _tenant = new(Guid.NewGuid());
    private static readonly UserId _user = new(Guid.NewGuid());

    private static string ValidHash() => new('a', PasswordResetToken.TokenHashLength);

    [Fact]
    public void Issue_ValidInput_InitializesDefaults()
    {
        var expiresAt = _fixedNow.UtcDateTime.AddHours(1);

        var token = PasswordResetToken.Issue(_tenant, _user, ValidHash(), expiresAt, _time);

        token.Id.Value.Should().NotBe(Guid.Empty);
        token.TenantId.Should().Be(_tenant);
        token.UserId.Should().Be(_user);
        token.TokenHash.Should().Be(ValidHash());
        token.ExpiresAt.Should().Be(expiresAt);
        token.CreatedAt.Should().Be(_fixedNow.UtcDateTime);
        token.UsedAt.Should().BeNull();
    }

    [Fact]
    public void Issue_EmptyTenant_Throws()
    {
        var act = () => PasswordResetToken.Issue(TenantId.Empty, _user, ValidHash(), _fixedNow.UtcDateTime.AddHours(1), _time);

        act.Should().Throw<ArgumentException>().WithParameterName("tenantId");
    }

    [Fact]
    public void Issue_EmptyUser_Throws()
    {
        var act = () => PasswordResetToken.Issue(_tenant, UserId.Empty, ValidHash(), _fixedNow.UtcDateTime.AddHours(1), _time);

        act.Should().Throw<ArgumentException>().WithParameterName("userId");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Issue_BlankHash_Throws(string? hash)
    {
        var act = () => PasswordResetToken.Issue(_tenant, _user, hash!, _fixedNow.UtcDateTime.AddHours(1), _time);

        act.Should().Throw<ArgumentException>().WithParameterName("tokenHash");
    }

    [Fact]
    public void Issue_HashWrongLength_Throws()
    {
        var shortHash = new string('a', PasswordResetToken.TokenHashLength - 1);

        var act = () => PasswordResetToken.Issue(_tenant, _user, shortHash, _fixedNow.UtcDateTime.AddHours(1), _time);

        act.Should().Throw<ArgumentException>().WithParameterName("tokenHash");
    }

    [Fact]
    public void Issue_ExpiresInPast_Throws()
    {
        var past = _fixedNow.UtcDateTime.AddSeconds(-1);

        var act = () => PasswordResetToken.Issue(_tenant, _user, ValidHash(), past, _time);

        act.Should().Throw<ArgumentException>().WithParameterName("expiresAt");
    }

    [Fact]
    public void Issue_ExpiresAtNow_Throws()
    {
        // Mez "expiresAt > now" — striktní, exact-now je expired (předem rejected
        // místo accept + okamžitý timeout v IsRedeemable).
        var act = () => PasswordResetToken.Issue(_tenant, _user, ValidHash(), _fixedNow.UtcDateTime, _time);

        act.Should().Throw<ArgumentException>().WithParameterName("expiresAt");
    }

    [Fact]
    public void IsRedeemable_FreshToken_True()
    {
        var token = PasswordResetToken.Issue(_tenant, _user, ValidHash(), _fixedNow.UtcDateTime.AddHours(1), _time);

        token.IsRedeemable(_fixedNow.UtcDateTime).Should().BeTrue();
    }

    [Fact]
    public void IsRedeemable_ExpiredToken_False()
    {
        var token = PasswordResetToken.Issue(_tenant, _user, ValidHash(), _fixedNow.UtcDateTime.AddHours(1), _time);
        var afterExpiration = _fixedNow.UtcDateTime.AddHours(2);

        token.IsRedeemable(afterExpiration).Should().BeFalse();
    }

    [Fact]
    public void IsRedeemable_UsedToken_False()
    {
        var token = PasswordResetToken.Issue(_tenant, _user, ValidHash(), _fixedNow.UtcDateTime.AddHours(1), _time);
        token.MarkUsed(_time);

        token.IsRedeemable(_fixedNow.UtcDateTime).Should().BeFalse();
    }

    [Fact]
    public void MarkUsed_RecordsUsedAt()
    {
        var token = PasswordResetToken.Issue(_tenant, _user, ValidHash(), _fixedNow.UtcDateTime.AddHours(1), _time);

        token.MarkUsed(_time);

        token.UsedAt.Should().Be(_fixedNow.UtcDateTime);
    }

    [Fact]
    public void MarkUsed_Twice_Throws()
    {
        var token = PasswordResetToken.Issue(_tenant, _user, ValidHash(), _fixedNow.UtcDateTime.AddHours(1), _time);
        token.MarkUsed(_time);

        var act = () => token.MarkUsed(_time);

        act.Should().Throw<InvalidOperationException>();
    }
}
