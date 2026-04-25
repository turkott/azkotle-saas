using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Inspections;
using FluentAssertions;

namespace AzKotle.Domain.Tests;

[Trait("Category", "Unit")]
public class InspectionTests
{
    private static readonly DateTimeOffset _fixedNow = new(2026, 04, 25, 12, 00, 00, TimeSpan.Zero);
    private static readonly FakeTimeProvider _time = new(_fixedNow);
    private static readonly TenantId _tenantId = TenantId.New();
    private static readonly BoilerId _boilerId = BoilerId.New();
    private static readonly UserId _technicianId = UserId.New();

    private static Inspection NewDraft() => Inspection.Draft(
        _tenantId, _boilerId, _technicianId,
        InspectionType.AnnualNv191,
        _fixedNow.UtcDateTime.AddHours(-2),
        _time);

    [Fact]
    public void Draft_ValidInput_InitializesAsDraftStatus()
    {
        var inspection = NewDraft();

        inspection.Id.Value.Should().NotBe(Guid.Empty);
        inspection.TenantId.Should().Be(_tenantId);
        inspection.BoilerId.Should().Be(_boilerId);
        inspection.TechnicianId.Should().Be(_technicianId);
        inspection.Type.Should().Be(InspectionType.AnnualNv191);
        inspection.Status.Should().Be(InspectionStatus.Draft);
        inspection.FormDataJson.Should().Be("{}");
        inspection.PdfB2Key.Should().BeNull();
        inspection.SignedAt.Should().BeNull();
    }

    [Fact]
    public void Draft_RaisesInspectionDrafted()
    {
        var inspection = NewDraft();

        inspection.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<InspectionDrafted>();
    }

    [Fact]
    public void Draft_FuturePerformedAt_Throws()
    {
        var act = () => Inspection.Draft(_tenantId, _boilerId, _technicianId,
            InspectionType.AnnualNv191, _fixedNow.UtcDateTime.AddDays(1), _time);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateFormData_OnDraft_StoresJson()
    {
        var inspection = NewDraft();
        inspection.UpdateFormData("{\"chamber_pressure_mbar\":18}", _time);
        inspection.FormDataJson.Should().Be("{\"chamber_pressure_mbar\":18}");
        inspection.UpdatedAt.Should().Be(_fixedNow.UtcDateTime);
    }

    [Fact]
    public void UpdateNarrative_NextDueBeforePerformed_Throws()
    {
        var inspection = NewDraft();
        var due = DateOnly.FromDateTime(inspection.PerformedAt).AddDays(-1);
        var act = () => inspection.UpdateNarrative("ok", null, due, _time);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Sign_ValidArgs_TransitionsToSigned()
    {
        var inspection = NewDraft();
        var sha = new string('a', 64);
        inspection.Sign("tenants/x/inspections/2026/y.pdf", sha, signatureData: null, _time);

        inspection.Status.Should().Be(InspectionStatus.Signed);
        inspection.PdfB2Key.Should().Be("tenants/x/inspections/2026/y.pdf");
        inspection.PdfSha256.Should().Be(sha);
        inspection.SignedAt.Should().Be(_fixedNow.UtcDateTime);
        inspection.DomainEvents.Should().Contain(e => e is InspectionSigned);
    }

    [Fact]
    public void Sign_TwiceThrows()
    {
        var inspection = NewDraft();
        var sha = new string('b', 64);
        inspection.Sign("k", sha, null, _time);
        var act = () => inspection.Sign("k2", sha, null, _time);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Sign_BadSha_Throws()
    {
        var inspection = NewDraft();
        var act = () => inspection.Sign("k", "tooShort", null, _time);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Archive_OnlyAllowedAfterSign()
    {
        var inspection = NewDraft();
        var act = () => inspection.Archive(_time);
        act.Should().Throw<InvalidOperationException>();

        inspection.Sign("k", new string('c', 64), null, _time);
        inspection.Archive(_time);
        inspection.Status.Should().Be(InspectionStatus.Archived);
    }
}
