using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace AzKotle.Api.IntegrationTests.MultiTenancy;

public sealed class TenantResolutionTests : IClassFixture<AzKotleApiFactory>
{
    private readonly AzKotleApiFactory _factory;

    public TenantResolutionTests(AzKotleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Health_Is_Allowed_Without_Tenant()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task WhoAmI_Without_Tenant_Returns_400()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/whoami");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("tenant_required");
    }

    [Fact]
    public async Task WhoAmI_With_Jwt_Claim_Returns_Tenant()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            AzKotleApiFactory.TestTenantClaimHeader,
            _factory.TenantAId.Value.ToString());

        var response = await client.GetAsync("/whoami");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("tenantId").GetGuid().Should().Be(_factory.TenantAId.Value);
    }

    [Fact]
    public async Task WhoAmI_With_Tenant_Subdomain_Returns_Tenant()
    {
        using var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Host = $"{_factory.TenantBSlug}.az-kotle.cz";

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("tenantId").GetGuid().Should().Be(_factory.TenantBId.Value);
    }

    [Fact]
    public async Task WhoAmI_With_Reserved_Subdomain_Returns_400()
    {
        using var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Host = "app.az-kotle.cz";

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task WhoAmI_With_Unknown_Subdomain_Returns_400()
    {
        using var client = _factory.CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Host = "nonexistent.az-kotle.cz";

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Jwt_Claim_Takes_Precedence_Over_Subdomain()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(
            AzKotleApiFactory.TestTenantClaimHeader,
            _factory.TenantAId.Value.ToString());
        var request = new HttpRequestMessage(HttpMethod.Get, "/whoami");
        request.Headers.Host = $"{_factory.TenantBSlug}.az-kotle.cz";
        request.Headers.Add(
            AzKotleApiFactory.TestTenantClaimHeader,
            _factory.TenantAId.Value.ToString());

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("tenantId").GetGuid().Should().Be(_factory.TenantAId.Value);
    }
}
