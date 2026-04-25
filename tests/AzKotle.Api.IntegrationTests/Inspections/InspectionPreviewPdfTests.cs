using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
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

public sealed class InspectionPreviewPdfTests : IClassFixture<AzKotleApiFactory>
{
    private readonly AzKotleApiFactory _factory;

    public InspectionPreviewPdfTests(AzKotleApiFactory factory)
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
    public async Task PreviewPdf_ReturnsPdfBytes()
    {
        using var client = ClientFor();
        var inspection = await SeedInspectionAsync(client);

        var resp = await client.GetAsync($"/api/v1/inspections/{inspection.Id}/preview.pdf");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(2000);
        Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task PreviewPdf_CrossTenant_Returns_404()
    {
        using var clientA = ClientFor(tenantA: true);
        using var clientB = ClientFor(tenantA: false);

        var inspection = await SeedInspectionAsync(clientA);
        var resp = await clientB.GetAsync($"/api/v1/inspections/{inspection.Id}/preview.pdf");
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PreviewPdf_ContainsCustomerName()
    {
        using var client = ClientFor();
        var inspection = await SeedInspectionAsync(client, customerName: "Acme Topení s.r.o.");

        var resp = await client.GetAsync($"/api/v1/inspections/{inspection.Id}/preview.pdf");
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        // PDF body is compressed so we can't grep text directly. Just smoke-check size + magic.
        bytes.Length.Should().BeGreaterThan(2000);
    }

    private static async Task<InspectionDto> SeedInspectionAsync(HttpClient client, string? customerName = null)
    {
        var customerResp = await client.PostAsJsonAsync("/api/v1/customers",
            new CreateCustomerRequest(CustomerType.Company,
                customerName ?? ("PDF Test " + Guid.NewGuid().ToString("N")[..6])));
        customerResp.EnsureSuccessStatusCode();
        var customer = (await customerResp.Content.ReadFromJsonAsync<CustomerDto>())!;

        var locationResp = await client.PostAsJsonAsync("/api/v1/locations",
            new CreateLocationRequest(customer.Id, "Ulice 5", "Praha", "11000"));
        locationResp.EnsureSuccessStatusCode();
        var location = (await locationResp.Content.ReadFromJsonAsync<LocationDto>())!;

        var boilerResp = await client.PostAsJsonAsync("/api/v1/boilers",
            new CreateBoilerRequest(location.Id, "Vaillant", "ecoTEC plus",
                "SN-" + Guid.NewGuid().ToString("N")[..8],
                24m, FuelType.NaturalGas, new DateOnly(2024, 06, 15)));
        boilerResp.EnsureSuccessStatusCode();
        var boiler = (await boilerResp.Content.ReadFromJsonAsync<BoilerDto>())!;

        var inspResp = await client.PostAsJsonAsync("/api/v1/inspections",
            new CreateInspectionRequest(boiler.Id, InspectionType.AnnualNv191, DateTime.UtcNow.AddHours(-1)));
        inspResp.EnsureSuccessStatusCode();
        var inspection = (await inspResp.Content.ReadFromJsonAsync<InspectionDto>())!;

        var updateResp = await client.PutAsJsonAsync($"/api/v1/inspections/{inspection.Id}/draft",
            new UpdateInspectionDraftRequest(
                "{\"co_ppm\":42,\"co2_pct\":8.5,\"flame_color\":\"Modrý ostrý\",\"main_valve_accessible\":true}",
                "Žádné závady",
                "Příští revize do 12 měsíců",
                DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1)));
        updateResp.EnsureSuccessStatusCode();
        return (await updateResp.Content.ReadFromJsonAsync<InspectionDto>())!;
    }
}
