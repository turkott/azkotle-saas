using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Locations;
using FluentAssertions;

namespace AzKotle.Domain.Tests;

[Trait("Category", "Unit")]
public class LocationTests
{
    private static readonly DateTimeOffset _fixedNow = new(2026, 04, 24, 12, 00, 00, TimeSpan.Zero);
    private static readonly FakeTimeProvider _time = new(_fixedNow);
    private static readonly TenantId _tenantId = TenantId.New();
    private static readonly CustomerId _customerId = CustomerId.New();

    [Fact]
    public void Create_ValidInput_InitializesState()
    {
        var location = Location.Create(_tenantId, _customerId, "  Radlická 3294/10  ", "  Praha  ", "  150 00  ", _time);

        location.Id.Value.Should().NotBe(Guid.Empty);
        location.TenantId.Should().Be(_tenantId);
        location.CustomerId.Should().Be(_customerId);
        location.Street.Should().Be("Radlická 3294/10");
        location.City.Should().Be("Praha");
        location.Zip.Should().Be("150 00");
        location.Gps.Should().BeNull();
        location.Notes.Should().BeNull();
        location.CreatedAt.Should().Be(_fixedNow.UtcDateTime);
    }

    [Fact]
    public void Create_RaisesLocationCreated()
    {
        var location = Location.Create(_tenantId, _customerId, "Ulice 1", "Praha", "11000", _time);

        location.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<LocationCreated>()
            .Which.Should().BeEquivalentTo(new
            {
                LocationId = location.Id,
                TenantId = _tenantId,
                CustomerId = _customerId,
                OccurredAt = _fixedNow.UtcDateTime,
            });
    }

    [Theory]
    [InlineData("", "Praha", "11000")]
    [InlineData("Ulice 1", "", "11000")]
    [InlineData("Ulice 1", "Praha", "")]
    public void Create_EmptyAddressPart_Throws(string street, string city, string zip)
    {
        var act = () => Location.Create(_tenantId, _customerId, street, city, zip, _time);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_EmptyTenantId_Throws()
    {
        var act = () => Location.Create(TenantId.Empty, _customerId, "Ulice", "Praha", "11000", _time);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_EmptyCustomerId_Throws()
    {
        var act = () => Location.Create(_tenantId, CustomerId.Empty, "Ulice", "Praha", "11000", _time);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void UpdateAddress_UpdatesFieldsAndTouches()
    {
        var location = Location.Create(_tenantId, _customerId, "Stará", "Brno", "60200", _time);
        location.UpdateAddress("Nová 5", "Praha", "11000", _time);

        location.Street.Should().Be("Nová 5");
        location.City.Should().Be("Praha");
        location.Zip.Should().Be("11000");
        location.UpdatedAt.Should().Be(_fixedNow.UtcDateTime);
    }

    [Fact]
    public void SetGps_ValidCoordinates_Stores()
    {
        var location = Location.Create(_tenantId, _customerId, "Ulice", "Praha", "11000", _time);
        location.SetGps(50.0874m, 14.4213m, _time);

        location.Gps.Should().NotBeNull();
        location.Gps!.Value.Latitude.Should().Be(50.0874m);
        location.Gps.Value.Longitude.Should().Be(14.4213m);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    public void SetGps_OutOfRange_Throws(double lat, double lon)
    {
        var location = Location.Create(_tenantId, _customerId, "Ulice", "Praha", "11000", _time);
        var act = () => location.SetGps((decimal)lat, (decimal)lon, _time);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ClearGps_ResetsToNull()
    {
        var location = Location.Create(_tenantId, _customerId, "Ulice", "Praha", "11000", _time);
        location.SetGps(50m, 14m, _time);
        location.ClearGps(_time);
        location.Gps.Should().BeNull();
    }

    [Fact]
    public void SetNotes_NormalizesEmptyToNull()
    {
        var location = Location.Create(_tenantId, _customerId, "Ulice", "Praha", "11000", _time);
        location.SetNotes("   ", _time);
        location.Notes.Should().BeNull();
        location.SetNotes("2. patro", _time);
        location.Notes.Should().Be("2. patro");
    }
}
