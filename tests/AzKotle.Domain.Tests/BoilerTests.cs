using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Boilers;
using FluentAssertions;

namespace AzKotle.Domain.Tests;

[Trait("Category", "Unit")]
public class BoilerTests
{
    private static readonly DateTimeOffset _fixedNow = new(2026, 04, 24, 12, 00, 00, TimeSpan.Zero);
    private static readonly FakeTimeProvider _time = new(_fixedNow);
    private static readonly TenantId _tenantId = TenantId.New();
    private static readonly LocationId _locationId = LocationId.New();
    private static readonly DateOnly _installedAt = new(2024, 06, 15);

    private static Boiler NewBoiler() => Boiler.Register(
        _tenantId,
        _locationId,
        qrCode: "AK-A1B2-C3",
        manufacturer: "Vaillant",
        model: "ecoTEC plus VU 246/5-5",
        serialNo: "SN-ABC-123",
        outputKw: 24.5m,
        fuelType: FuelType.NaturalGas,
        installedAt: _installedAt,
        timeProvider: _time);

    [Fact]
    public void Register_ValidInput_InitializesState()
    {
        var boiler = NewBoiler();

        boiler.Id.Value.Should().NotBe(Guid.Empty);
        boiler.TenantId.Should().Be(_tenantId);
        boiler.LocationId.Should().Be(_locationId);
        boiler.QrCode.Should().Be("AK-A1B2-C3");
        boiler.Manufacturer.Should().Be("Vaillant");
        boiler.Model.Should().Be("ecoTEC plus VU 246/5-5");
        boiler.SerialNo.Should().Be("SN-ABC-123");
        boiler.OutputKw.Should().Be(24.5m);
        boiler.FuelType.Should().Be(FuelType.NaturalGas);
        boiler.InstalledAt.Should().Be(_installedAt);
        boiler.LastInspectionAt.Should().BeNull();
        boiler.NextInspectionDue.Should().BeNull();
        boiler.CreatedAt.Should().Be(_fixedNow.UtcDateTime);
    }

    [Fact]
    public void Register_RaisesBoilerRegistered()
    {
        var boiler = NewBoiler();

        boiler.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<BoilerRegistered>()
            .Which.Should().BeEquivalentTo(new
            {
                BoilerId = boiler.Id,
                TenantId = _tenantId,
                LocationId = _locationId,
                QrCode = "AK-A1B2-C3",
                OccurredAt = _fixedNow.UtcDateTime,
            });
    }

    [Theory]
    [InlineData("")]
    [InlineData("AK-ABCD")]
    [InlineData("XX-ABCD-12")]
    [InlineData("AK-IIII-12")] // I is not in Crockford Base32
    [InlineData("AK-LLLL-12")] // L is not in Crockford Base32
    [InlineData("AK-ABCDE-12")] // wrong length
    public void Register_InvalidQrCode_Throws(string qrCode)
    {
        var act = () => Boiler.Register(_tenantId, _locationId, qrCode, "M", "Mdl", "SN",
            10m, FuelType.NaturalGas, _installedAt, _time);
        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1.5)]
    [InlineData(10000)]
    public void Register_InvalidOutputKw_Throws(double output)
    {
        var act = () => Boiler.Register(_tenantId, _locationId, "AK-A1B2-C3",
            "M", "Mdl", "SN", (decimal)output, FuelType.NaturalGas, _installedAt, _time);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Register_FutureInstallationDate_Throws()
    {
        var future = DateOnly.FromDateTime(_fixedNow.UtcDateTime).AddDays(1);
        var act = () => Boiler.Register(_tenantId, _locationId, "AK-A1B2-C3",
            "M", "Mdl", "SN", 10m, FuelType.NaturalGas, future, _time);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RecordInspection_ValidInput_UpdatesAndRaisesEvent()
    {
        var boiler = NewBoiler();
        var performed = new DateOnly(2026, 04, 20);
        var nextDue = performed.AddYears(1);

        boiler.RecordInspection(performed, nextDue, _time);

        boiler.LastInspectionAt.Should().Be(performed);
        boiler.NextInspectionDue.Should().Be(nextDue);
        boiler.UpdatedAt.Should().Be(_fixedNow.UtcDateTime);
        boiler.DomainEvents.Should().Contain(e => e is BoilerInspectionRecorded);
    }

    [Fact]
    public void RecordInspection_BeforeInstallation_Throws()
    {
        var boiler = NewBoiler();
        var earlier = _installedAt.AddDays(-1);
        var act = () => boiler.RecordInspection(earlier, null, _time);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RecordInspection_FuturePerformed_Throws()
    {
        var boiler = NewBoiler();
        var future = DateOnly.FromDateTime(_fixedNow.UtcDateTime).AddDays(1);
        var act = () => boiler.RecordInspection(future, null, _time);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void RecordInspection_NextDueBeforePerformed_Throws()
    {
        var boiler = NewBoiler();
        var performed = new DateOnly(2026, 04, 20);
        var act = () => boiler.RecordInspection(performed, performed.AddDays(-1), _time);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateSpecs_ValidInput_UpdatesAndTouches()
    {
        var boiler = NewBoiler();
        boiler.UpdateSpecs("Bosch", "Condens 2500", "SN-456", 30m, FuelType.Lpg, _time);

        boiler.Manufacturer.Should().Be("Bosch");
        boiler.Model.Should().Be("Condens 2500");
        boiler.SerialNo.Should().Be("SN-456");
        boiler.OutputKw.Should().Be(30m);
        boiler.FuelType.Should().Be(FuelType.Lpg);
        boiler.UpdatedAt.Should().Be(_fixedNow.UtcDateTime);
    }

    [Fact]
    public void MoveToLocation_ValidId_UpdatesLocation()
    {
        var boiler = NewBoiler();
        var newLocation = LocationId.New();
        boiler.MoveToLocation(newLocation, _time);
        boiler.LocationId.Should().Be(newLocation);
    }

    [Fact]
    public void MoveToLocation_EmptyId_Throws()
    {
        var boiler = NewBoiler();
        var act = () => boiler.MoveToLocation(LocationId.Empty, _time);
        act.Should().Throw<ArgumentException>();
    }
}
