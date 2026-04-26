using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using AzKotle.Api.IntegrationTests.MultiTenancy;
using AzKotle.Application.Boilers;
using AzKotle.Application.Customers;
using AzKotle.Application.Inspections;
using AzKotle.Application.Locations;
using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Boilers;
using AzKotle.Domain.Entities.Customers;
using AzKotle.Domain.Entities.Inspections;
using AzKotle.Domain.Entities.Users;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AzKotle.Api.IntegrationTests.Inspections;

public sealed class SignInspectionTests : IClassFixture<AzKotleApiFactory>
{
    private readonly AzKotleApiFactory _factory;

    public SignInspectionTests(AzKotleApiFactory factory)
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
    public async Task Sign_HappyPath_TransitionsAndUploadsPdf()
    {
        using var client = ClientFor();
        var inspection = await SeedInspectionAsync(client);

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/inspections/{inspection.Id}/sign",
            new SignInspectionRequest(SignatureBase64: null));
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await resp.Content.ReadFromJsonAsync<SignedInspectionResponse>();
        body.Should().NotBeNull();
        body!.Inspection.Status.Should().Be(InspectionStatus.Signed);
        body.Inspection.PdfB2Key.Should().NotBeNullOrWhiteSpace();
        body.Inspection.PdfSha256.Should().NotBeNullOrWhiteSpace();
        body.Inspection.PdfSha256!.Length.Should().Be(64);
        body.Inspection.SignedAt.Should().NotBeNull();
        body.PdfSha256.Should().Be(body.Inspection.PdfSha256);

        // Verify storage object exists and SHA matches
        await using var stream = await _factory.TestStorage.GetAsync(body.Inspection.PdfB2Key!);
        stream.Should().NotBeNull();
        using var ms = new MemoryStream();
        await stream!.CopyToAsync(ms);
        var bytes = ms.ToArray();
        bytes.Length.Should().BeGreaterThan(2000);
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant().Should().Be(body.PdfSha256);
    }

    [Fact]
    public async Task Sign_WritesAuditLogRow()
    {
        using var client = ClientFor();
        var inspection = await SeedInspectionAsync(client);

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/inspections/{inspection.Id}/sign",
            new SignInspectionRequest(null));
        resp.EnsureSuccessStatusCode();

        await using var db = _factory.CreateAdminDbContext();
        var log = await db.AuditLog.AsNoTracking()
            .FirstOrDefaultAsync(a => a.TargetType == "inspection"
                && a.TargetId == inspection.Id
                && a.Action == "inspection.signed");
        log.Should().NotBeNull();
        log!.TenantId.Should().Be(_factory.TenantAId);
        log.ActorUserId.Should().Be(_factory.UserAId);
        log.MetadataJson.Should().Contain("pdf_sha256");
    }

    [Fact]
    public async Task Sign_Twice_Returns_400()
    {
        using var client = ClientFor();
        var inspection = await SeedInspectionAsync(client);

        var first = await client.PostAsJsonAsync(
            $"/api/v1/inspections/{inspection.Id}/sign", new SignInspectionRequest(null));
        first.EnsureSuccessStatusCode();

        var second = await client.PostAsJsonAsync(
            $"/api/v1/inspections/{inspection.Id}/sign", new SignInspectionRequest(null));
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sign_NonExistent_Returns_404()
    {
        using var client = ClientFor();
        var resp = await client.PostAsJsonAsync(
            $"/api/v1/inspections/{Guid.NewGuid()}/sign",
            new SignInspectionRequest(null));
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sign_CrossTenant_Returns_404()
    {
        using var clientA = ClientFor(tenantA: true);
        using var clientB = ClientFor(tenantA: false);

        var inspection = await SeedInspectionAsync(clientA);

        var resp = await clientB.PostAsJsonAsync(
            $"/api/v1/inspections/{inspection.Id}/sign",
            new SignInspectionRequest(null));
        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Sign_InvalidSignatureBase64_Returns_400()
    {
        using var client = ClientFor();
        var inspection = await SeedInspectionAsync(client);

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/inspections/{inspection.Id}/sign",
            new SignInspectionRequest(SignatureBase64: "not_base_64!!!"));
        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Sign_KeyFollowsTenantsPathConvention()
    {
        using var client = ClientFor();
        var inspection = await SeedInspectionAsync(client);

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/inspections/{inspection.Id}/sign",
            new SignInspectionRequest(null));
        resp.EnsureSuccessStatusCode();
        var body = (await resp.Content.ReadFromJsonAsync<SignedInspectionResponse>())!;

        body.Inspection.PdfB2Key.Should().StartWith($"tenants/{_factory.TenantAId.Value:D}/inspections/");
        body.Inspection.PdfB2Key.Should().EndWith($"/{inspection.Id:D}.pdf");
    }

    private static async Task<InspectionDto> SeedInspectionAsync(HttpClient client)
    {
        var customerResp = await client.PostAsJsonAsync("/api/v1/customers",
            new CreateCustomerRequest(CustomerType.Company, "Sign Test " + Guid.NewGuid().ToString("N")[..6]));
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
            new CreateInspectionRequest(boiler.Id, InspectionType.AnnualNv191, DateTime.UtcNow.AddHours(-1)));
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
