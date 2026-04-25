using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Audit;
using FluentAssertions;

namespace AzKotle.Domain.Tests;

[Trait("Category", "Unit")]
public class AuditLogTests
{
    private static readonly DateTimeOffset _fixedNow = new(2026, 04, 25, 12, 00, 00, TimeSpan.Zero);
    private static readonly FakeTimeProvider _time = new(_fixedNow);
    private static readonly TenantId _tenantId = TenantId.New();
    private static readonly UserId _actorId = UserId.New();
    private static readonly Guid _targetId = Guid.NewGuid();

    [Fact]
    public void Record_ValidInput_PopulatesAllFields()
    {
        var log = AuditLog.Record(
            _tenantId, _actorId, "inspection.signed", "inspection", _targetId,
            ipAddress: "203.0.113.42",
            userAgent: "Mozilla/5.0",
            metadataJson: "{\"sha\":\"abc\"}",
            timeProvider: _time);

        log.Id.Value.Should().NotBe(Guid.Empty);
        log.TenantId.Should().Be(_tenantId);
        log.ActorUserId.Should().Be(_actorId);
        log.Action.Should().Be("inspection.signed");
        log.TargetType.Should().Be("inspection");
        log.TargetId.Should().Be(_targetId);
        log.IpAddress.Should().Be("203.0.113.42");
        log.UserAgent.Should().Be("Mozilla/5.0");
        log.MetadataJson.Should().Be("{\"sha\":\"abc\"}");
        log.OccurredAt.Should().Be(_fixedNow.UtcDateTime);
    }

    [Fact]
    public void Record_NullActor_AllowedForSystemActions()
    {
        var log = AuditLog.Record(_tenantId, actorUserId: null,
            "system.cleanup", "system", null, null, null, null, _time);
        log.ActorUserId.Should().BeNull();
    }

    [Fact]
    public void Record_EmptyActorIdNormalizedToNull()
    {
        var log = AuditLog.Record(_tenantId, UserId.Empty,
            "system.cleanup", "system", null, null, null, null, _time);
        log.ActorUserId.Should().BeNull();
    }

    [Fact]
    public void Record_EmptyTenantId_Throws()
    {
        var act = () => AuditLog.Record(TenantId.Empty, _actorId,
            "x", "y", null, null, null, null, _time);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Record_EmptyAction_Throws(string action)
    {
        var act = () => AuditLog.Record(_tenantId, _actorId,
            action, "y", null, null, null, null, _time);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Record_TooLongAction_Throws()
    {
        var act = () => AuditLog.Record(_tenantId, _actorId,
            new string('a', AuditLog.ActionMaxLength + 1), "y", null, null, null, null, _time);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Record_LongUserAgent_Truncates()
    {
        var ua = new string('U', AuditLog.UserAgentMaxLength + 50);
        var log = AuditLog.Record(_tenantId, _actorId,
            "x", "y", null, null, ua, null, _time);
        log.UserAgent!.Length.Should().Be(AuditLog.UserAgentMaxLength);
    }

    [Fact]
    public void Record_TooLongIpAddress_Throws()
    {
        var act = () => AuditLog.Record(_tenantId, _actorId,
            "x", "y", null, new string('1', AuditLog.IpAddressMaxLength + 1), null, null, _time);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Record_BlankOptionalFields_NormalizedToNull()
    {
        var log = AuditLog.Record(_tenantId, _actorId,
            "x", "y", null,
            ipAddress: "   ",
            userAgent: "",
            metadataJson: "  ",
            _time);
        log.IpAddress.Should().BeNull();
        log.UserAgent.Should().BeNull();
        log.MetadataJson.Should().BeNull();
    }
}
