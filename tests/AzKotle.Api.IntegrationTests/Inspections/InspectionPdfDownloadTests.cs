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
using AzKotle.Domain.Entities.Users;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AzKotle.Api.IntegrationTests.Inspections;

public sealed class InspectionPdfDownloadTests : IClassFixture<AzKotleApiFactory>
{
    private readonly AzKotleApiFactory _factory;

    public InspectionPdfDownloadTests(AzKotleApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(bool tenantA = true)
    {
        // ClientOptions: do NOT auto-follow redirects so we can assert 302 + Location.
        var client = _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
        });
        var token = _factory.IssueJwt(
            tenantA ? _factory.TenantAId : _factory.TenantBId,
            tenantA ? _factory.UserAId : _factory.UserBId,
            tenantA ? "a@example.com" : "b@example.com",
            UserRole.Owner);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task Download_AfterSign_Returns302WithPresignedLocationAndAuditRow()
    {
        using var client = ClientFor();
        var inspection = await SeedSignedInspectionAsync(client);

        var resp = await client.GetAsync($"/api/v1/inspections/{inspection.Id}/pdf");

        resp.StatusCode.Should().Be(HttpStatusCode.Redirect);
        resp.Headers.Location.Should().NotBeNull();
        resp.Headers.Location!.ToString().Should().Contain("X-Amz-Signature");
        resp.Headers.Location.ToString().Should().Contain("response-content-disposition");

        await using var db = _factory.CreateAdminDbContext();
        var log = await db.AuditLog.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TargetType == "inspection"
                && a.TargetId == inspection.Id
                && a.Action == "inspection.pdf_url_issued");
        log.Should().NotBeNull();
        log!.TenantId.Should().Be(_factory.TenantAId);
        log.ActorUserId.Should().Be(_factory.UserAId);
        log.MetadataJson.Should().Contain("ttl_seconds").And.Contain("pdf_b2_key");
    }

    [Fact]
    public async Task Download_DraftInspection_Returns404()
    {
        using var client = ClientFor();
        var inspection = await SeedDraftInspectionAsync(client);

        var resp = await client.GetAsync($"/api/v1/inspections/{inspection.Id}/pdf");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Download_CrossTenant_Returns404()
    {
        using var clientA = ClientFor(tenantA: true);
        using var clientB = ClientFor(tenantA: false);

        var inspection = await SeedSignedInspectionAsync(clientA);

        var resp = await clientB.GetAsync($"/api/v1/inspections/{inspection.Id}/pdf");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Download_NonExistent_Returns404()
    {
        using var client = ClientFor();

        var resp = await client.GetAsync($"/api/v1/inspections/{Guid.NewGuid()}/pdf");

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Download_TwiceWritesTwoAuditRows()
    {
        using var client = ClientFor();
        var inspection = await SeedSignedInspectionAsync(client);

        (await client.GetAsync($"/api/v1/inspections/{inspection.Id}/pdf")).StatusCode.Should().Be(HttpStatusCode.Redirect);
        (await client.GetAsync($"/api/v1/inspections/{inspection.Id}/pdf")).StatusCode.Should().Be(HttpStatusCode.Redirect);

        await using var db = _factory.CreateAdminDbContext();
        var count = await db.AuditLog.AsNoTracking()
            .CountAsync(a => a.TargetType == "inspection"
                && a.TargetId == inspection.Id
                && a.Action == "inspection.pdf_url_issued");
        count.Should().Be(2);
    }

    private static async Task<InspectionDto> SeedSignedInspectionAsync(HttpClient client)
    {
        var inspection = await SeedDraftInspectionAsync(client);
        var signResp = await client.PostAsJsonAsync(
            $"/api/v1/inspections/{inspection.Id}/sign",
            new SignInspectionRequest(SignatureBase64: null));
        signResp.EnsureSuccessStatusCode();
        var signed = (await signResp.Content.ReadFromJsonAsync<SignedInspectionResponse>())!;
        return signed.Inspection;
    }

    private static async Task<InspectionDto> SeedDraftInspectionAsync(HttpClient client)
    {
        var customerResp = await client.PostAsJsonAsync("/api/v1/customers",
            new CreateCustomerRequest(CustomerType.Company, "PdfDl Test " + Guid.NewGuid().ToString("N")[..6]));
        customerResp.EnsureSuccessStatusCode();
        var customer = (await customerResp.Content.ReadFromJsonAsync<CustomerDto>())!;

        var locationResp = await client.PostAsJsonAsync("/api/v1/locations",
            new CreateLocationRequest(customer.Id, "Ulice 7", "Praha", "11000"));
        locationResp.EnsureSuccessStatusCode();
        var location = (await locationResp.Content.ReadFromJsonAsync<LocationDto>())!;

        var boilerResp = await client.PostAsJsonAsync("/api/v1/boilers",
            new CreateBoilerRequest(location.Id, "Vaillant", "ecoTEC plus",
                "SN-" + Guid.NewGuid().ToString("N")[..8],
                24m, FuelType.NaturalGas, new DateOnly(2024, 06, 15)));
        boilerResp.EnsureSuccessStatusCode();
        var boiler = (await boilerResp.Content.ReadFromJsonAsync<BoilerDto>())!;

        var inspResp = await client.PostAsJsonAsync("/api/v1/inspections",
            new Application.Inspections.CreateInspectionRequest(
                boiler.Id,
                Domain.Entities.Inspections.InspectionType.AnnualNv191,
                DateTime.UtcNow.AddHours(-1)));
        inspResp.EnsureSuccessStatusCode();
        var inspection = (await inspResp.Content.ReadFromJsonAsync<InspectionDto>())!;

        var updateResp = await client.PutAsJsonAsync($"/api/v1/inspections/{inspection.Id}/draft",
            new UpdateInspectionDraftRequest(
                "{\"co_ppm\":42,\"co2_pct\":8.5,\"flame_color\":\"Modrý ostrý\"}",
                "Žádné závady",
                "Příští revize do 12 měsíců",
                DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1)));
        updateResp.EnsureSuccessStatusCode();
        return (await updateResp.Content.ReadFromJsonAsync<InspectionDto>())!;
    }
}
