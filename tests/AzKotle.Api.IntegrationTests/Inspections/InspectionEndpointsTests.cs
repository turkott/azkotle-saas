using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AzKotle.Api.IntegrationTests.MultiTenancy;
using AzKotle.Application.Boilers;
using AzKotle.Application.Customers;
using AzKotle.Application.Inspections;
using AzKotle.Application.Locations;
using AzKotle.Domain.Entities.Boilers;
using AzKotle.Domain.Entities.Customers;
using AzKotle.Domain.Entities.Inspections;
using AzKotle.Domain.Entities.Users;
using FluentAssertions;

namespace AzKotle.Api.IntegrationTests.Inspections;

public sealed class InspectionEndpointsTests : IClassFixture<AzKotleApiFactory>
{
    private readonly AzKotleApiFactory _factory;

    public InspectionEndpointsTests(AzKotleApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(bool tenantA = true)
    {
        var client = _factory.CreateClient();
        var token = _factory.IssueJwt(
            tenantA ? _factory.TenantAId : _factory.TenantBId,
            tenantA ? _factory.UserAId : _factory.UserBId,
            tenantA ? "a@example.com" : "b@example.com",
            UserRole.Owner);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Create_Then_UpdateDraft_HappyPath()
    {
        using var client = ClientFor();
        var boiler = await SeedBoilerAsync(client);

        var createResp = await client.PostAsJsonAsync("/api/v1/inspections",
            new CreateInspectionRequest(boiler.Id, InspectionType.AnnualNv191, DateTime.UtcNow.AddHours(-1)));
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var inspection = await createResp.Content.ReadFromJsonAsync<InspectionDto>();
        inspection!.Status.Should().Be(InspectionStatus.Draft);
        inspection.FormDataJson.Should().Be("{}");

        var updateResp = await client.PutAsJsonAsync($"/api/v1/inspections/{inspection.Id}/draft",
            new UpdateInspectionDraftRequest(
                FormDataJson: "{\"co_ppm\":42,\"flue_drag_pa\":12}",
                Findings: "Doporučujeme očistit hořák",
                Recommendations: "Příští revize do roka",
                NextDueAt: DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1)));
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<InspectionDto>();
        updated!.FormDataJson.Should().Contain("co_ppm");
        updated.Findings.Should().Be("Doporučujeme očistit hořák");
    }

    [Fact]
    public async Task Create_FuturePerformedAt_Returns_400()
    {
        using var client = ClientFor();
        var boiler = await SeedBoilerAsync(client);

        var resp = await client.PostAsJsonAsync("/api/v1/inspections",
            new CreateInspectionRequest(boiler.Id, InspectionType.AnnualNv191, DateTime.UtcNow.AddDays(7)));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_NonExistentBoiler_Returns_404()
    {
        using var client = ClientFor();
        var resp = await client.PostAsJsonAsync("/api/v1/inspections",
            new CreateInspectionRequest(Guid.NewGuid(), InspectionType.AnnualNv191, DateTime.UtcNow.AddHours(-1)));
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CrossTenant_Returns_404()
    {
        using var clientA = ClientFor(tenantA: true);
        using var clientB = ClientFor(tenantA: false);

        var boiler = await SeedBoilerAsync(clientA);
        var createResp = await clientA.PostAsJsonAsync("/api/v1/inspections",
            new CreateInspectionRequest(boiler.Id, InspectionType.AnnualNv191, DateTime.UtcNow.AddHours(-1)));
        var inspection = await createResp.Content.ReadFromJsonAsync<InspectionDto>();

        var crossGet = await clientB.GetAsync($"/api/v1/inspections/{inspection!.Id}");
        crossGet.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var crossUpdate = await clientB.PutAsJsonAsync($"/api/v1/inspections/{inspection.Id}/draft",
            new UpdateInspectionDraftRequest("{}", null, null, null));
        crossUpdate.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ListByBoiler_FilterWorks()
    {
        using var client = ClientFor();
        var boiler = await SeedBoilerAsync(client);

        for (var i = 0; i < 3; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/v1/inspections",
                new CreateInspectionRequest(boiler.Id, InspectionType.AnnualNv191, DateTime.UtcNow.AddHours(-2 - i)));
            resp.EnsureSuccessStatusCode();
        }

        var list = await client.GetAsync($"/api/v1/inspections?boilerId={boiler.Id}");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await list.Content.ReadFromJsonAsync<AzKotle.Application.Common.PagedResponse<InspectionDto>>();
        page!.Items.Should().HaveCountGreaterThanOrEqualTo(3);
        page.Items.Should().AllSatisfy(i => i.BoilerId.Should().Be(boiler.Id));
    }

    private static async Task<BoilerDto> SeedBoilerAsync(HttpClient client)
    {
        var customerResp = await client.PostAsJsonAsync("/api/v1/customers",
            new CreateCustomerRequest(CustomerType.Company, "Insp Test " + Guid.NewGuid().ToString("N")[..6]));
        customerResp.EnsureSuccessStatusCode();
        var customer = (await customerResp.Content.ReadFromJsonAsync<CustomerDto>())!;

        var locationResp = await client.PostAsJsonAsync("/api/v1/locations",
            new CreateLocationRequest(customer.Id, "Ulice 1", "Praha", "11000"));
        locationResp.EnsureSuccessStatusCode();
        var location = (await locationResp.Content.ReadFromJsonAsync<LocationDto>())!;

        var boilerResp = await client.PostAsJsonAsync("/api/v1/boilers",
            new CreateBoilerRequest(location.Id, "Vaillant", "ecoTEC",
                "SN-" + Guid.NewGuid().ToString("N")[..8],
                24m, FuelType.NaturalGas, new DateOnly(2024, 01, 01)));
        boilerResp.EnsureSuccessStatusCode();
        return (await boilerResp.Content.ReadFromJsonAsync<BoilerDto>())!;
    }
}
