using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Customers;
using FluentAssertions;

namespace AzKotle.Domain.Tests;

[Trait("Category", "Unit")]
public class CustomerTests
{
    private static readonly DateTimeOffset _fixedNow = new(2026, 04, 24, 12, 00, 00, TimeSpan.Zero);
    private static readonly FakeTimeProvider _time = new(_fixedNow);
    private static readonly TenantId _tenantId = TenantId.New();

    [Fact]
    public void Create_Person_ValidInput_InitializesState()
    {
        var customer = Customer.Create(_tenantId, CustomerType.Person, "  Jan Novák  ", _time);

        customer.Id.Value.Should().NotBe(Guid.Empty);
        customer.TenantId.Should().Be(_tenantId);
        customer.Type.Should().Be(CustomerType.Person);
        customer.Name.Should().Be("Jan Novák");
        customer.Ico.Should().BeNull();
        customer.Email.Should().BeNull();
        customer.Phone.Should().BeNull();
        customer.Notes.Should().BeNull();
        customer.CreatedAt.Should().Be(_fixedNow.UtcDateTime);
        customer.UpdatedAt.Should().BeNull();
    }

    [Fact]
    public void Create_RaisesCustomerCreated()
    {
        var customer = Customer.Create(_tenantId, CustomerType.Company, "ACME s.r.o.", _time);

        customer.DomainEvents.Should().ContainSingle()
            .Which.Should().BeOfType<CustomerCreated>()
            .Which.Should().BeEquivalentTo(new
            {
                CustomerId = customer.Id,
                TenantId = _tenantId,
                Type = CustomerType.Company,
                Name = "ACME s.r.o.",
                OccurredAt = _fixedNow.UtcDateTime,
            });
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_EmptyName_Throws(string name)
    {
        var act = () => Customer.Create(_tenantId, CustomerType.Person, name, _time);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_TooLongName_Throws()
    {
        var name = new string('x', Customer.NameMaxLength + 1);
        var act = () => Customer.Create(_tenantId, CustomerType.Person, name, _time);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Create_EmptyTenantId_Throws()
    {
        var act = () => Customer.Create(TenantId.Empty, CustomerType.Person, "Jan", _time);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetIco_CompanyWith8Digits_Accepts()
    {
        var customer = Customer.Create(_tenantId, CustomerType.Company, "ACME s.r.o.", _time);
        customer.SetIco("12345678", _time);
        customer.Ico.Should().Be("12345678");
        customer.UpdatedAt.Should().Be(_fixedNow.UtcDateTime);
    }

    [Fact]
    public void SetIco_Person_Throws()
    {
        var customer = Customer.Create(_tenantId, CustomerType.Person, "Jan", _time);
        var act = () => customer.SetIco("12345678", _time);
        act.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("1234567")]
    [InlineData("123456789")]
    [InlineData("abcdefgh")]
    public void SetIco_InvalidFormat_Throws(string ico)
    {
        var customer = Customer.Create(_tenantId, CustomerType.Company, "ACME", _time);
        var act = () => customer.SetIco(ico, _time);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SetIco_Null_ClearsValue()
    {
        var customer = Customer.Create(_tenantId, CustomerType.Company, "ACME", _time);
        customer.SetIco("12345678", _time);
        customer.SetIco(null, _time);
        customer.Ico.Should().BeNull();
    }

    [Fact]
    public void SetContactInfo_NormalizesEmail()
    {
        var customer = Customer.Create(_tenantId, CustomerType.Person, "Jan", _time);
        customer.SetContactInfo("  Jan@Example.CZ  ", " +420 123 456 789 ", _time);
        customer.Email.Should().Be("jan@example.cz");
        customer.Phone.Should().Be("+420 123 456 789");
    }

    [Fact]
    public void SetContactInfo_InvalidEmail_Throws()
    {
        var customer = Customer.Create(_tenantId, CustomerType.Person, "Jan", _time);
        var act = () => customer.SetContactInfo("not-an-email", null, _time);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Rename_UpdatesNameAndTouches()
    {
        var customer = Customer.Create(_tenantId, CustomerType.Person, "Starý", _time);
        customer.Rename("  Nový  ", _time);
        customer.Name.Should().Be("Nový");
        customer.UpdatedAt.Should().Be(_fixedNow.UtcDateTime);
    }

    [Fact]
    public void SetNotes_NormalizesEmptyToNull()
    {
        var customer = Customer.Create(_tenantId, CustomerType.Person, "Jan", _time);
        customer.SetNotes("   ", _time);
        customer.Notes.Should().BeNull();
        customer.SetNotes("poznámka", _time);
        customer.Notes.Should().Be("poznámka");
    }
}
