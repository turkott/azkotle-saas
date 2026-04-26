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
using Microsoft.EntityFrameworkCore;

namespace AzKotle.Api.IntegrationTests.Inspections;

public sealed class InspectionConcurrencyTests : IClassFixture<AzKotleApiFactory>
{
    private readonly AzKotleApiFactory _factory;

    public InspectionConcurrencyTests(AzKotleApiFactory factory)
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
    public async Task Sign_StaleVersion_AfterAutosave_Returns_409()
    {
        using var client = ClientFor();
        var inspection = await SeedInspectionAsync(client);
        var staleVersion = inspection.Version;

        // Simulate a concurrent autosave that bumps xmin in the DB.
        var refreshed = await UpdateDraftAsync(client, inspection,
            "{\"co_ppm\":99,\"co2_pct\":9.0,\"flame_color\":\"Modrý ostrý\"}");
        refreshed.Version.Should().NotBe(staleVersion);

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/inspections/{inspection.Id}/sign",
            new SignInspectionRequest(SignatureBase64: null, Version: staleVersion));

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Sign_StaleVersion_DoesNotUploadPdfToStorage()
    {
        using var client = ClientFor();
        var inspection = await SeedInspectionAsync(client);
        var staleVersion = inspection.Version;

        await UpdateDraftAsync(client, inspection,
            "{\"co_ppm\":1,\"co2_pct\":1.0,\"flame_color\":\"Modrý ostrý\"}");

        var resp = await client.PostAsJsonAsync(
            $"/api/v1/inspections/{inspection.Id}/sign",
            new SignInspectionRequest(SignatureBase64: null, Version: staleVersion));
        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // The expected S3 key would be tenants/{tid}/inspections/{yyyy}/{iid}.pdf — verify no orphan.
        var key = $"tenants/{_factory.TenantAId.Value:D}/inspections/{DateTime.UtcNow:yyyy}/{inspection.Id:D}.pdf";
        await using var stream = await _factory.TestStorage.GetAsync(key);
        stream.Should().BeNull("stale-version sign must reject before any S3 PUT");
    }

    [Fact]
    public async Task UpdateDraft_StaleVersion_Returns_409()
    {
        using var client = ClientFor();
        var inspection = await SeedInspectionAsync(client);
        var staleVersion = inspection.Version;

        var refreshed = await UpdateDraftAsync(client, inspection,
            "{\"co_ppm\":50,\"co2_pct\":5.0,\"flame_color\":\"Modrý ostrý\"}");
        refreshed.Version.Should().NotBe(staleVersion);

        var resp = await client.PutAsJsonAsync($"/api/v1/inspections/{inspection.Id}/draft",
            new UpdateInspectionDraftRequest(
                "{\"co_ppm\":999}", null, null, null, staleVersion));

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task UpdateDraft_FreshVersion_Succeeds_AndBumpsVersion()
    {
        using var client = ClientFor();
        var inspection = await SeedInspectionAsync(client);

        var updated = await UpdateDraftAsync(client, inspection,
            "{\"co_ppm\":77,\"co2_pct\":7.7,\"flame_color\":\"Modrý ostrý\"}");

        updated.Version.Should().NotBe(inspection.Version);
        updated.FormDataJson.Should().Contain("\"co_ppm\":77");
    }

    [Fact]
    public async Task Sign_TwoParallelRequests_OneSucceeds_OneIs409_NoOrphan()
    {
        using var client = ClientFor();
        var inspection = await SeedInspectionAsync(client);

        // Both requests carry the same version — exactly one must win, the other
        // must block on the row lock and then 409 once the winner commits.
        var taskA = client.PostAsJsonAsync(
            $"/api/v1/inspections/{inspection.Id}/sign",
            new SignInspectionRequest(null, inspection.Version));
        var taskB = client.PostAsJsonAsync(
            $"/api/v1/inspections/{inspection.Id}/sign",
            new SignInspectionRequest(null, inspection.Version));

        var responses = await Task.WhenAll(taskA, taskB);
        var statuses = responses.Select(r => (int)r.StatusCode).OrderBy(s => s).ToArray();
        statuses.Should().BeEquivalentTo(new[] { 200, 409 });

        // Storage holds exactly one PDF, and its SHA matches what the winner wrote
        // to the DB. Race did not produce a SHA/content mismatch.
        var winner = responses.First(r => r.StatusCode == HttpStatusCode.OK);
        var body = (await winner.Content.ReadFromJsonAsync<SignedInspectionResponse>())!;
        await using var stream = await _factory.TestStorage.GetAsync(body.Inspection.PdfB2Key!);
        stream.Should().NotBeNull();
        using var ms = new MemoryStream();
        await stream!.CopyToAsync(ms);
        var bytes = ms.ToArray();
        bytes.Length.Should().BeGreaterThan(2000);
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes))
            .ToLowerInvariant().Should().Be(body.PdfSha256);
    }

    private static async Task<InspectionDto> UpdateDraftAsync(HttpClient client, InspectionDto current, string formJson)
    {
        var resp = await client.PutAsJsonAsync($"/api/v1/inspections/{current.Id}/draft",
            new UpdateInspectionDraftRequest(formJson, null, null,
                DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1), current.Version));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<InspectionDto>())!;
    }

    private static async Task<InspectionDto> SeedInspectionAsync(HttpClient client)
    {
        var customerResp = await client.PostAsJsonAsync("/api/v1/customers",
            new CreateCustomerRequest(CustomerType.Company, "Concur Test " + Guid.NewGuid().ToString("N")[..6]));
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
                DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1),
                inspection.Version));
        updateResp.EnsureSuccessStatusCode();
        return (await updateResp.Content.ReadFromJsonAsync<InspectionDto>())!;
    }
}
