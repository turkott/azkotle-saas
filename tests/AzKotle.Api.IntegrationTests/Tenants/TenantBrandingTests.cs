using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
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
using AzKotle.Infrastructure.Tenants;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AzKotle.Api.IntegrationTests.Tenants;

public sealed class TenantBrandingTests : IClassFixture<AzKotleApiFactory>
{
    // 1×1 transparent PNG.
    private static readonly byte[] _validPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNkYAAAAAYAAjCB0C8AAAAASUVORK5CYII=");

    // Minimal JPEG (start-of-image + JFIF + end-of-image — enough to pass magic bytes check).
    private static readonly byte[] _validJpeg =
    {
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01,
        0x00, 0x01, 0x00, 0x00, 0xFF, 0xD9,
    };

    private readonly AzKotleApiFactory _factory;

    public TenantBrandingTests(AzKotleApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(UserRole role = UserRole.Owner, bool tenantA = true)
    {
        var client = _factory.CreateClient();
        var token = _factory.IssueJwt(
            tenantA ? _factory.TenantAId : _factory.TenantBId,
            tenantA ? _factory.UserAId : _factory.UserBId,
            tenantA ? "a@example.com" : "b@example.com",
            role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static MultipartFormDataContent FileForm(byte[] bytes, string contentType, string fileName)
    {
        var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
        form.Add(content, "file", fileName);
        return form;
    }

    [Fact]
    public async Task UploadLogo_HappyPath_StoresInS3AndUpdatesTenant()
    {
        using var client = ClientFor();
        using var form = FileForm(_validPng, "image/png", "logo.png");

        var resp = await client.PostAsync("/api/v1/tenant/branding/logo", form);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadFromJsonAsync<TenantBrandingResponseDto>();
        body.Should().NotBeNull();
        body!.LogoStorageKey.Should().Be(TenantBrandingService.BuildKey(_factory.TenantAId));

        await using var stream = await _factory.TestStorage.GetAsync(body.LogoStorageKey);
        stream.Should().NotBeNull();

        await using var db = _factory.CreateAdminDbContext();
        var tenant = await db.Tenants.AsNoTracking().FirstAsync(t => t.Id == _factory.TenantAId);
        tenant.LogoStorageKey.Should().Be(body.LogoStorageKey);
        tenant.LogoUpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UploadLogo_WritesAuditRow()
    {
        using var client = ClientFor();
        using var form = FileForm(_validPng, "image/png", "logo.png");

        var resp = await client.PostAsync("/api/v1/tenant/branding/logo", form);
        resp.EnsureSuccessStatusCode();

        await using var db = _factory.CreateAdminDbContext();
        var log = await db.AuditLog.AsNoTracking().FirstOrDefaultAsync(a =>
            a.TargetType == "tenant"
            && a.TargetId == _factory.TenantAId.Value
            && a.Action == "tenant.branding_updated");
        log.Should().NotBeNull();
        log!.ActorUserId.Should().Be(_factory.UserAId);
        log.MetadataJson.Should().Contain("logo_storage_key").And.Contain("image/png");
    }

    [Fact]
    public async Task UploadLogo_NonOwner_Returns_403()
    {
        using var client = ClientFor(role: UserRole.Technician);
        using var form = FileForm(_validPng, "image/png", "logo.png");

        var resp = await client.PostAsync("/api/v1/tenant/branding/logo", form);

        resp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UploadLogo_TooBig_Returns_400()
    {
        using var client = ClientFor();
        var oversize = new byte[TenantBrandingService.MaxLogoBytes + 1];
        Array.Copy(_validPng, oversize, _validPng.Length);
        using var form = FileForm(oversize, "image/png", "huge.png");

        var resp = await client.PostAsync("/api/v1/tenant/branding/logo", form);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadLogo_UnsupportedContentType_Returns_400()
    {
        using var client = ClientFor();
        using var form = FileForm(_validPng, "application/pdf", "logo.pdf");

        var resp = await client.PostAsync("/api/v1/tenant/branding/logo", form);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadLogo_ContentMagicMismatch_Returns_400()
    {
        using var client = ClientFor();
        // Claim PNG content-type but provide JPEG magic bytes.
        using var form = FileForm(_validJpeg, "image/png", "fake.png");

        var resp = await client.PostAsync("/api/v1/tenant/branding/logo", form);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task UploadLogo_JpegAccepted()
    {
        using var client = ClientFor();
        using var form = FileForm(_validJpeg, "image/jpeg", "logo.jpg");

        var resp = await client.PostAsync("/api/v1/tenant/branding/logo", form);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task PreviewPdf_AfterLogoUpload_EmbedsLogoBytes()
    {
        using var client = ClientFor();
        var inspection = await SeedDraftInspectionAsync(client);

        var beforePdf = await client.GetByteArrayAsync($"/api/v1/inspections/{inspection.Id}/preview.pdf");

        using var form = FileForm(_validPng, "image/png", "logo.png");
        (await client.PostAsync("/api/v1/tenant/branding/logo", form)).EnsureSuccessStatusCode();

        var afterPdf = await client.GetByteArrayAsync($"/api/v1/inspections/{inspection.Id}/preview.pdf");

        // QuestPDF embeds raster images in the document. With the logo present,
        // the rendered PDF must be strictly larger than the same PDF without it.
        afterPdf.Length.Should().BeGreaterThan(beforePdf.Length,
            "PDF rendered after logo upload should embed the logo bytes");
    }

    [Fact]
    public async Task UploadLogo_TwiceOverwritesSameKey()
    {
        using var client = ClientFor();

        var first = await client.PostAsync("/api/v1/tenant/branding/logo",
            FileForm(_validPng, "image/png", "v1.png"));
        first.EnsureSuccessStatusCode();
        var firstBody = (await first.Content.ReadFromJsonAsync<TenantBrandingResponseDto>())!;

        var second = await client.PostAsync("/api/v1/tenant/branding/logo",
            FileForm(_validJpeg, "image/jpeg", "v2.jpg"));
        second.EnsureSuccessStatusCode();
        var secondBody = (await second.Content.ReadFromJsonAsync<TenantBrandingResponseDto>())!;

        secondBody.LogoStorageKey.Should().Be(firstBody.LogoStorageKey,
            "fixed key strategy means re-upload overwrites in place");
        secondBody.LogoUpdatedAt.Should().BeAfter(firstBody.LogoUpdatedAt);
    }

    private static async Task<InspectionDto> SeedDraftInspectionAsync(HttpClient client)
    {
        var customerResp = await client.PostAsJsonAsync("/api/v1/customers",
            new CreateCustomerRequest(CustomerType.Company, "Brand Test " + Guid.NewGuid().ToString("N")[..6]));
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
                "{\"co_ppm\":42,\"co2_pct\":8.5}",
                "Žádné závady", "Příští revize do 12 měsíců",
                DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1),
                inspection.Version));
        updateResp.EnsureSuccessStatusCode();
        return (await updateResp.Content.ReadFromJsonAsync<InspectionDto>())!;
    }

    private sealed record TenantBrandingResponseDto(string LogoStorageKey, DateTime LogoUpdatedAt);
}
