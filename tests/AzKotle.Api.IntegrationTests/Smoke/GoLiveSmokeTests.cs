using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AzKotle.Api.IntegrationTests.MultiTenancy;
using AzKotle.Application.Auth;
using AzKotle.Application.Boilers;
using AzKotle.Application.Customers;
using AzKotle.Application.Inspections;
using AzKotle.Application.Locations;
using AzKotle.Domain.Entities.Boilers;
using AzKotle.Domain.Entities.Customers;
using AzKotle.Domain.Entities.Inspections;
using FluentAssertions;

namespace AzKotle.Api.IntegrationTests.Smoke;

/// <summary>
/// Pre-go-live end-to-end smoke. Drives the full happy path a real customer
/// hits on day one: register company → create customer/boiler → start NV 191
/// inspection with a (mock) photo reference → sign. Signing pulls
/// <see cref="AzKotle.Infrastructure.Pdf.InspectionReportBuilder"/> into
/// QuestPDF rendering, which is the production failure mode we most fear
/// (font/layout exceptions on Linux). Spurious S3 GET for the missing photo
/// gracefully falls back per F12 contract — PDF still renders.
/// </summary>
[Trait("Category", "Smoke")]
public sealed class GoLiveSmokeTests : IClassFixture<AzKotleApiFactory>
{
    private readonly AzKotleApiFactory _factory;

    public GoLiveSmokeTests(AzKotleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FullFlow_RegisterToSignedPdf_succeeds()
    {
        using var client = _factory.CreateClient();

        // 1. Register a brand new tenant + Owner. TenantSlug = null exercises the
        // F20 auto-slug path. ICO is a mock 8-digit string; uniqueness check passes
        // because the factory bootstraps tenants with different ICOs (12345678 is
        // already taken by Tenant A — use 99999999 to avoid collision).
        var registerResp = await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(
            Email: "smoke-go-live@example.test",
            Password: "GoLiveSmokeStrong!2026",
            FullName: "Pavel Smokovský",
            TenantSlug: null,
            CompanyName: "Smoke Test Servis s.r.o.",
            Ico: "99999999"));
        registerResp.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "fresh tenant + owner + auto-slug must succeed");

        var auth = await registerResp.Content.ReadFromJsonAsync<AuthResponse>();
        auth.Should().NotBeNull();
        auth!.Role.Should().Be("Owner");

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);

        // 2. Create customer (resolves to current tenant via JWT tenant_id claim).
        var customerResp = await client.PostAsJsonAsync("/api/v1/customers", new CreateCustomerRequest(
            Type: CustomerType.Person,
            Name: "Jan Novák",
            Email: "jan@example.test",
            Phone: "+420 777 111 222"));
        customerResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var customer = await customerResp.Content.ReadFromJsonAsync<CustomerDto>();
        customer.Should().NotBeNull();

        // 3. Create location for this customer.
        var locationResp = await client.PostAsJsonAsync("/api/v1/locations", new CreateLocationRequest(
            CustomerId: customer!.Id,
            Street: "Husova 12",
            City: "Praha",
            Zip: "11000"));
        locationResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var location = await locationResp.Content.ReadFromJsonAsync<LocationDto>();

        // 4. Create boiler at the location.
        var boilerResp = await client.PostAsJsonAsync("/api/v1/boilers", new CreateBoilerRequest(
            LocationId: location!.Id,
            Manufacturer: "Vaillant",
            Model: "ecoTEC pro VU 246/5",
            SerialNo: "SN-SMOKE-001",
            OutputKw: 24.0m,
            FuelType: FuelType.NaturalGas,
            InstalledAt: new DateOnly(2020, 6, 15)));
        boilerResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var boiler = await boilerResp.Content.ReadFromJsonAsync<BoilerDto>();

        // 5. Start AnnualNv191 draft inspection.
        var draftResp = await client.PostAsJsonAsync("/api/v1/inspections", new CreateInspectionRequest(
            BoilerId: boiler!.Id,
            Type: InspectionType.AnnualNv191,
            PerformedAt: DateTime.UtcNow));
        draftResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var draft = await draftResp.Content.ReadFromJsonAsync<InspectionDto>();
        draft.Should().NotBeNull();
        draft!.Status.Should().Be(InspectionStatus.Draft);
        draft.AccessHash.Should().NotBeNullOrEmpty(because: "F14 — every draft gets an unguessable hash for the public link");

        // 6. Update draft with a mock photo S3 key + minimal fields. The S3 key
        // points to an object that DOES NOT EXIST in the test bucket — the F12
        // graceful-fallback contract says photo fetch failure must NOT block the
        // PDF; the gallery section just renders with zero photos. This test
        // explicitly covers that fallback path.
        var mockPhotoKey = $"tenants/{auth.TenantId:D}/inspections/{draft.Id:D}/photos/photo_burner.jpg";
        var formDataJson = $$"""
        {
            "weather": "Slunečno",
            "operator_present": true,
            "main_valve_accessible": true,
            "main_valve_marked": true,
            "supply_pipe_corrosion": "Žádná",
            "burner_condition": "Vyhovující",
            "ignition_electrode_ok": true,
            "flame_color": "Modrý ostrý",
            "chamber_pressure_mbar": 18.5,
            "flue_drag_pa": 12.5,
            "flue_visual_ok": true,
            "co_ppm": 50,
            "ionization_test_pass": true,
            "thermostat_test_pass": true,
            "pressure_relief_test_pass": true,
            "fresh_air_intake_ok": true,
            "exhaust_path_clear": true,
            "result": "Vyhovuje",
            "photo_burner": "{{mockPhotoKey}}"
        }
        """;
        var updateResp = await client.PutAsJsonAsync($"/api/v1/inspections/{draft.Id}/draft", new UpdateInspectionDraftRequest(
            FormDataJson: formDataJson,
            Findings: "Bez závad.",
            Recommendations: "Pravidelná roční kontrola.",
            NextDueAt: DateOnly.FromDateTime(DateTime.UtcNow.AddYears(1)),
            Version: draft.Version));
        updateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await updateResp.Content.ReadFromJsonAsync<InspectionDto>();

        // 7. Sign — this is the production failure mode we most want to catch:
        //    InspectionSignService → InspectionReportBuilder.RenderAsync → QuestPDF
        //    document.GeneratePdf() in the Linux-flavored test container. If
        //    QuestPDF can't find a usable font (Lato bundled), or if any layout
        //    constraint blows up, this throws and the test fails loudly.
        var signResp = await client.PostAsJsonAsync($"/api/v1/inspections/{draft.Id}/sign", new SignInspectionRequest(
            SignatureBase64: null,
            Version: updated!.Version));
        signResp.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "QuestPDF on Linux must render without font/layout exceptions");

        var signed = await signResp.Content.ReadFromJsonAsync<SignedInspectionResponse>();
        signed.Should().NotBeNull();
        signed!.PdfSha256.Should().HaveLength(64, because: "SHA-256 hex is exactly 64 chars");
        signed.Inspection.Status.Should().Be(InspectionStatus.Signed);
        signed.Inspection.PdfB2Key.Should().NotBeNullOrEmpty();

        // 8. Smoke the public link path (F14) — verifies the SECURITY DEFINER
        // function works end-to-end and the report is reachable without auth.
        client.DefaultRequestHeaders.Authorization = null;
        var publicResp = await client.GetAsync($"/api/v1/public/inspections/{signed.Inspection.AccessHash}");
        publicResp.StatusCode.Should().Be(HttpStatusCode.OK,
            because: "F14 public viewer must return summary for the just-signed inspection");
    }
}
