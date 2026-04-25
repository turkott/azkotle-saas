using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AzKotle.Api.IntegrationTests.MultiTenancy;
using AzKotle.Application.Boilers;
using AzKotle.Application.Customers;
using AzKotle.Application.Locations;
using AzKotle.Domain.Entities.Boilers;
using AzKotle.Domain.Entities.Customers;
using AzKotle.Domain.Entities.Users;
using FluentAssertions;

namespace AzKotle.Api.IntegrationTests.QrCodes;

public sealed class QrCodeEndpointsTests : IClassFixture<AzKotleApiFactory>
{
    private readonly AzKotleApiFactory _factory;

    public QrCodeEndpointsTests(AzKotleApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor(bool tenantA = true)
    {
        var client = _factory.CreateClient(new()
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
    public async Task BoilerQrPng_ReturnsPngOctets()
    {
        using var client = ClientFor();
        var boiler = await SeedBoilerAsync(client);

        var resp = await client.GetAsync($"/api/v1/boilers/{boiler.Id}/qr.png");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(100);
        // PNG magic header
        bytes[..8].Should().Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
    }

    [Fact]
    public async Task BoilerQrLabel_ReturnsPdf()
    {
        using var client = ClientFor();
        var boiler = await SeedBoilerAsync(client);

        var resp = await client.GetAsync($"/api/v1/boilers/{boiler.Id}/qr-label?copies=4");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/pdf");
        var bytes = await resp.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(1000);
        System.Text.Encoding.ASCII.GetString(bytes, 0, 4).Should().Be("%PDF");
    }

    [Fact]
    public async Task BoilerQr_CrossTenant_Returns_404()
    {
        using var clientA = ClientFor(tenantA: true);
        using var clientB = ClientFor(tenantA: false);
        var boiler = await SeedBoilerAsync(clientA);

        var pngResp = await clientB.GetAsync($"/api/v1/boilers/{boiler.Id}/qr.png");
        pngResp.StatusCode.Should().Be(HttpStatusCode.NotFound);

        var pdfResp = await clientB.GetAsync($"/api/v1/boilers/{boiler.Id}/qr-label");
        pdfResp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task PublicQr_Anonymous_Redirects_To_Login()
    {
        using var client = _factory.CreateClient(new() { AllowAutoRedirect = false });

        var resp = await client.GetAsync("/qr/AK-XXXX-XX");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found, HttpStatusCode.SeeOther);
        var location = resp.Headers.Location?.ToString() ?? string.Empty;
        location.Should().Contain("/login");
        location.Should().Contain("AK-XXXX-XX");
    }

    [Fact]
    public async Task PublicQr_Authenticated_Redirects_To_BoilerDetail()
    {
        using var auth = ClientFor();
        var boiler = await SeedBoilerAsync(auth);

        var resp = await auth.GetAsync($"/qr/{boiler.QrCode}");
        resp.StatusCode.Should().BeOneOf(HttpStatusCode.Redirect, HttpStatusCode.Found, HttpStatusCode.SeeOther);
        var location = resp.Headers.Location?.ToString() ?? string.Empty;
        location.Should().Contain($"/boilers/{boiler.Id}");
    }

    private static async Task<BoilerDto> SeedBoilerAsync(HttpClient client)
    {
        var customerResp = await client.PostAsJsonAsync("/api/v1/customers",
            new CreateCustomerRequest(CustomerType.Company, "QR Test " + Guid.NewGuid().ToString("N")[..6]));
        customerResp.EnsureSuccessStatusCode();
        var customer = (await customerResp.Content.ReadFromJsonAsync<CustomerDto>())!;

        var locationResp = await client.PostAsJsonAsync("/api/v1/locations",
            new CreateLocationRequest(customer.Id, "Ulice 1", "Praha", "11000"));
        locationResp.EnsureSuccessStatusCode();
        var location = (await locationResp.Content.ReadFromJsonAsync<LocationDto>())!;

        var boilerResp = await client.PostAsJsonAsync("/api/v1/boilers",
            new CreateBoilerRequest(location.Id, "Vaillant", "ecoTEC", "SN-" + Guid.NewGuid().ToString("N")[..8],
                24m, FuelType.NaturalGas, new DateOnly(2024, 01, 01)));
        boilerResp.EnsureSuccessStatusCode();
        return (await boilerResp.Content.ReadFromJsonAsync<BoilerDto>())!;
    }
}
