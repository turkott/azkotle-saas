using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AzKotle.Api.IntegrationTests.MultiTenancy;
using AzKotle.Application.Boilers;
using AzKotle.Application.Common;
using AzKotle.Application.Customers;
using AzKotle.Application.Locations;
using AzKotle.Domain.Entities.Boilers;
using AzKotle.Domain.Entities.Customers;
using AzKotle.Domain.Entities.Users;
using FluentAssertions;

namespace AzKotle.Api.IntegrationTests.Crud;

public sealed class CrudEndpointsTests : IClassFixture<AzKotleApiFactory>
{
    private readonly AzKotleApiFactory _factory;

    public CrudEndpointsTests(AzKotleApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(bool tenantA = true)
    {
        var client = _factory.CreateClient();
        var tenantId = tenantA ? _factory.TenantAId : _factory.TenantBId;
        var userId = tenantA ? _factory.UserAId : _factory.UserBId;
        var email = tenantA ? "a@example.com" : "b@example.com";
        var token = _factory.IssueJwt(tenantId, userId, email, UserRole.Owner);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Customer_CRUD_HappyPath()
    {
        using var client = ClientFor();

        var create = await client.PostAsJsonAsync("/api/v1/customers",
            new CreateCustomerRequest(CustomerType.Company, "Test s.r.o.", "12345678", "info@test.cz", null, "VIP"));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var customer = await create.Content.ReadFromJsonAsync<CustomerDto>();
        customer.Should().NotBeNull();
        customer!.Name.Should().Be("Test s.r.o.");
        customer.Ico.Should().Be("12345678");
        customer.Email.Should().Be("info@test.cz");

        var get = await client.GetAsync($"/api/v1/customers/{customer.Id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);

        var update = await client.PutAsJsonAsync($"/api/v1/customers/{customer.Id}",
            new UpdateCustomerRequest("Test 2 s.r.o.", "12345678", null, "+420 111", null));
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await update.Content.ReadFromJsonAsync<CustomerDto>();
        updated!.Name.Should().Be("Test 2 s.r.o.");
        updated.Phone.Should().Be("+420 111");

        var list = await client.GetAsync("/api/v1/customers");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await list.Content.ReadFromJsonAsync<PagedResponse<CustomerDto>>();
        page!.Items.Should().Contain(c => c.Id == customer.Id);

        var delete = await client.DeleteAsync($"/api/v1/customers/{customer.Id}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getAfter = await client.GetAsync($"/api/v1/customers/{customer.Id}");
        getAfter.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Customer_CrossTenant_Returns_404()
    {
        using var clientA = ClientFor(tenantA: true);
        using var clientB = ClientFor(tenantA: false);

        var create = await clientA.PostAsJsonAsync("/api/v1/customers",
            new CreateCustomerRequest(CustomerType.Person, "Petr"));
        if (create.StatusCode != HttpStatusCode.Created)
        {
            var body = await create.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Create failed: {(int)create.StatusCode} {create.StatusCode}\n{body}");
        }
        var customer = await create.Content.ReadFromJsonAsync<CustomerDto>();

        var crossGet = await clientB.GetAsync($"/api/v1/customers/{customer!.Id}");
        crossGet.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var crossUpdate = await clientB.PutAsJsonAsync($"/api/v1/customers/{customer.Id}",
            new UpdateCustomerRequest("Hacked"));
        crossUpdate.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var crossDelete = await clientB.DeleteAsync($"/api/v1/customers/{customer.Id}");
        crossDelete.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var listB = await clientB.GetAsync("/api/v1/customers");
        if (!listB.IsSuccessStatusCode)
        {
            var body = await listB.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"List failed: {(int)listB.StatusCode}\n{body}");
        }
        var pageB = await listB.Content.ReadFromJsonAsync<PagedResponse<CustomerDto>>();
        pageB!.Items.Should().NotContain(c => c.Id == customer.Id);
    }

    [Fact]
    public async Task Customer_Validation_RejectsBadIco()
    {
        using var client = ClientFor();
        var resp = await client.PostAsJsonAsync("/api/v1/customers",
            new CreateCustomerRequest(CustomerType.Company, "ACME", "abc"));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Location_CRUD_HappyPath()
    {
        using var client = ClientFor();

        var customer = await CreateCustomerAsync(client);

        var create = await client.PostAsJsonAsync("/api/v1/locations",
            new CreateLocationRequest(customer.Id, "Radlická 3294/10", "Praha", "150 00", 50.0874m, 14.4213m, "Vchod B"));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var location = await create.Content.ReadFromJsonAsync<LocationDto>();
        location.Should().NotBeNull();
        location!.Street.Should().Be("Radlická 3294/10");
        location.GpsLatitude.Should().Be(50.0874m);

        var update = await client.PutAsJsonAsync($"/api/v1/locations/{location.Id}",
            new UpdateLocationRequest("Nová 5", "Brno", "60200", null, null, null));
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await update.Content.ReadFromJsonAsync<LocationDto>();
        updated!.City.Should().Be("Brno");
        updated.GpsLatitude.Should().BeNull();

        var listFiltered = await client.GetAsync($"/api/v1/locations?customerId={customer.Id}");
        var page = await listFiltered.Content.ReadFromJsonAsync<PagedResponse<LocationDto>>();
        page!.Items.Should().ContainSingle(l => l.Id == location.Id);

        var del = await client.DeleteAsync($"/api/v1/locations/{location.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Location_NonExistentCustomer_Returns_404()
    {
        using var client = ClientFor();
        var resp = await client.PostAsJsonAsync("/api/v1/locations",
            new CreateLocationRequest(Guid.NewGuid(), "Ulice", "Praha", "11000"));
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Boiler_CRUD_HappyPath_GeneratesQrAndAcceptsInspection()
    {
        using var client = ClientFor();
        var customer = await CreateCustomerAsync(client);
        var location = await CreateLocationAsync(client, customer.Id);

        var create = await client.PostAsJsonAsync("/api/v1/boilers",
            new CreateBoilerRequest(location.Id, "Vaillant", "ecoTEC plus", "SN-1",
                24.5m, FuelType.NaturalGas, new DateOnly(2024, 06, 15)));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var boiler = await create.Content.ReadFromJsonAsync<BoilerDto>();
        boiler.Should().NotBeNull();
        boiler!.QrCode.Should().MatchRegex("^AK-[0-9A-HJKMNP-TV-Z]{4}-[0-9A-HJKMNP-TV-Z]{2}$");
        boiler.LocationId.Should().Be(location.Id);

        var inspection = await client.PostAsJsonAsync($"/api/v1/boilers/{boiler.Id}/inspections",
            new RecordInspectionRequest(new DateOnly(2026, 04, 20), new DateOnly(2027, 04, 20)));
        inspection.StatusCode.Should().Be(HttpStatusCode.OK);
        var withInspection = await inspection.Content.ReadFromJsonAsync<BoilerDto>();
        withInspection!.LastInspectionAt.Should().Be(new DateOnly(2026, 04, 20));
        withInspection.NextInspectionDue.Should().Be(new DateOnly(2027, 04, 20));

        var update = await client.PutAsJsonAsync($"/api/v1/boilers/{boiler.Id}",
            new UpdateBoilerRequest("Bosch", "Condens 2500", "SN-1", 30m, FuelType.Lpg));
        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await update.Content.ReadFromJsonAsync<BoilerDto>();
        updated!.Manufacturer.Should().Be("Bosch");

        var del = await client.DeleteAsync($"/api/v1/boilers/{boiler.Id}");
        del.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Boiler_CrossTenant_Returns_404()
    {
        using var clientA = ClientFor(tenantA: true);
        using var clientB = ClientFor(tenantA: false);

        var customerA = await CreateCustomerAsync(clientA);
        var locationA = await CreateLocationAsync(clientA, customerA.Id);
        var boilerA = await CreateBoilerAsync(clientA, locationA.Id);

        var crossGet = await clientB.GetAsync($"/api/v1/boilers/{boilerA.Id}");
        crossGet.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var crossInspect = await clientB.PostAsJsonAsync($"/api/v1/boilers/{boilerA.Id}/inspections",
            new RecordInspectionRequest(new DateOnly(2026, 04, 20), null));
        crossInspect.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Pagination_Cursor_Walks_All_Items()
    {
        using var client = ClientFor();

        for (var i = 0; i < 5; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/v1/customers",
                new CreateCustomerRequest(CustomerType.Person, $"Page {i}"));
            resp.EnsureSuccessStatusCode();
        }

        var firstPage = await client.GetAsync("/api/v1/customers?pageSize=2");
        firstPage.StatusCode.Should().Be(HttpStatusCode.OK);
        var page1 = await firstPage.Content.ReadFromJsonAsync<PagedResponse<CustomerDto>>();
        page1!.Items.Should().HaveCount(2);
        page1.NextCursor.Should().NotBeNullOrEmpty();

        var secondPage = await client.GetAsync($"/api/v1/customers?pageSize=2&cursor={Uri.EscapeDataString(page1.NextCursor!)}");
        var page2 = await secondPage.Content.ReadFromJsonAsync<PagedResponse<CustomerDto>>();
        page2!.Items.Should().NotBeEmpty();
        page2.Items.Select(c => c.Id).Should().NotIntersectWith(page1.Items.Select(c => c.Id));
    }

    [Fact]
    public async Task Anonymous_Request_Returns_401()
    {
        using var anon = _factory.CreateClient();
        var resp = await anon.GetAsync("/api/v1/customers");
        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<CustomerDto> CreateCustomerAsync(HttpClient client, string? name = null)
    {
        var resp = await client.PostAsJsonAsync("/api/v1/customers",
            new CreateCustomerRequest(CustomerType.Company, name ?? $"Customer {Guid.NewGuid():N}"));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<CustomerDto>())!;
    }

    private static async Task<LocationDto> CreateLocationAsync(HttpClient client, Guid customerId)
    {
        var suffix = Guid.NewGuid().ToString("N")[..6];
        var resp = await client.PostAsJsonAsync("/api/v1/locations",
            new CreateLocationRequest(customerId, $"Ulice {suffix}", "Praha", "11000"));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<LocationDto>())!;
    }

    private static async Task<BoilerDto> CreateBoilerAsync(HttpClient client, Guid locationId)
    {
        var serial = "SN-" + Guid.NewGuid().ToString("N")[..16];
        var resp = await client.PostAsJsonAsync("/api/v1/boilers",
            new CreateBoilerRequest(locationId, "Vaillant", "ecoTEC", serial,
                24m, FuelType.NaturalGas, new DateOnly(2024, 01, 01)));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<BoilerDto>())!;
    }
}
