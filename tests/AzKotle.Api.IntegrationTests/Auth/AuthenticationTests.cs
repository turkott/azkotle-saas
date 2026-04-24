using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AzKotle.Api.IntegrationTests.MultiTenancy;
using AzKotle.Application.Abstractions;
using AzKotle.Application.Auth;
using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Auth;
using AzKotle.Domain.Entities.Users;
using AzKotle.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AzKotle.Api.IntegrationTests.Auth;

public sealed class AuthenticationTests : IClassFixture<AzKotleApiFactory>
{
    private readonly AzKotleApiFactory _factory;

    public AuthenticationTests(AzKotleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_Login_Refresh_Happy_Path()
    {
        using var client = _factory.CreateClient();
        var registerRequest = NewRegisterRequest("acme-happy", "acme@example.com");

        var registerResp = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        registerResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var registered = await registerResp.Content.ReadFromJsonAsync<AuthResponse>();
        registered.Should().NotBeNull();
        registered!.AccessToken.Should().NotBeNullOrEmpty();
        registered.RefreshToken.Should().NotBeNullOrEmpty();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registered.AccessToken);
        var whoamiResp = await client.GetAsync("/whoami");
        whoamiResp.StatusCode.Should().Be(HttpStatusCode.OK);

        client.DefaultRequestHeaders.Authorization = null;
        var loginResp = await SendWithTenantHostAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/login",
            registerRequest.TenantSlug,
            new LoginRequest(registerRequest.Email, registerRequest.Password));
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var logged = await loginResp.Content.ReadFromJsonAsync<AuthResponse>();
        logged!.AccessToken.Should().NotBeNullOrEmpty();

        var refreshResp = await SendWithTenantHostAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/refresh",
            registerRequest.TenantSlug,
            new RefreshRequest(logged.RefreshToken));
        refreshResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var refreshed = await refreshResp.Content.ReadFromJsonAsync<AuthResponse>();
        refreshed!.RefreshToken.Should().NotBe(logged.RefreshToken);
        refreshed.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Register_With_Duplicate_Email_Returns_409()
    {
        using var client = _factory.CreateClient();
        var first = NewRegisterRequest("dup-tenant-1", "duplicate@example.com");
        var second = first with { TenantSlug = "dup-tenant-2" };

        var firstResp = await client.PostAsJsonAsync("/api/v1/auth/register", first);
        firstResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondResp = await client.PostAsJsonAsync("/api/v1/auth/register", second);
        secondResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await secondResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("email_taken");
    }

    [Fact]
    public async Task Register_With_Duplicate_Slug_Returns_409()
    {
        using var client = _factory.CreateClient();
        var first = NewRegisterRequest("dup-slug", "first@example.com");
        var second = first with { Email = "second@example.com" };

        var firstResp = await client.PostAsJsonAsync("/api/v1/auth/register", first);
        firstResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var secondResp = await client.PostAsJsonAsync("/api/v1/auth/register", second);
        secondResp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await secondResp.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("error").GetString().Should().Be("slug_taken");
    }

    [Fact]
    public async Task Login_With_Wrong_Password_Returns_401()
    {
        using var client = _factory.CreateClient();
        var request = NewRegisterRequest("wrong-pwd", "wrong@example.com");
        await client.PostAsJsonAsync("/api/v1/auth/register", request);

        var loginResp = await SendWithTenantHostAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/login",
            request.TenantSlug,
            new LoginRequest(request.Email, "WrongPassword123!"));

        loginResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_With_Revoked_Token_Returns_401()
    {
        using var client = _factory.CreateClient();
        var request = NewRegisterRequest("revoked", "revoked@example.com");
        var registered = await (await client.PostAsJsonAsync("/api/v1/auth/register", request))
            .Content.ReadFromJsonAsync<AuthResponse>();

        var firstRefresh = await SendWithTenantHostAsync(
            client, HttpMethod.Post, "/api/v1/auth/refresh", request.TenantSlug,
            new RefreshRequest(registered!.RefreshToken));
        firstRefresh.StatusCode.Should().Be(HttpStatusCode.OK);

        var reuse = await SendWithTenantHostAsync(
            client, HttpMethod.Post, "/api/v1/auth/refresh", request.TenantSlug,
            new RefreshRequest(registered.RefreshToken));
        reuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_Reuse_Revokes_Entire_Chain()
    {
        using var client = _factory.CreateClient();
        var request = NewRegisterRequest("chain", "chain@example.com");
        var registered = await (await client.PostAsJsonAsync("/api/v1/auth/register", request))
            .Content.ReadFromJsonAsync<AuthResponse>();

        var second = await (await SendWithTenantHostAsync(
            client, HttpMethod.Post, "/api/v1/auth/refresh", request.TenantSlug,
            new RefreshRequest(registered!.RefreshToken))).Content.ReadFromJsonAsync<AuthResponse>();

        var third = await (await SendWithTenantHostAsync(
            client, HttpMethod.Post, "/api/v1/auth/refresh", request.TenantSlug,
            new RefreshRequest(second!.RefreshToken))).Content.ReadFromJsonAsync<AuthResponse>();

        // Reuse the first (already rotated) token -> should revoke chain including third
        var reuseResp = await SendWithTenantHostAsync(
            client, HttpMethod.Post, "/api/v1/auth/refresh", request.TenantSlug,
            new RefreshRequest(registered.RefreshToken));
        reuseResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The current (third) token should also be invalid now.
        var thirdReuse = await SendWithTenantHostAsync(
            client, HttpMethod.Post, "/api/v1/auth/refresh", request.TenantSlug,
            new RefreshRequest(third!.RefreshToken));
        thirdReuse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_Endpoint_Without_Bearer_Returns_401()
    {
        using var client = _factory.CreateClient();
        var loginResp = await SendWithTenantHostAsync(
            client,
            HttpMethod.Post,
            "/api/v1/auth/logout",
            _factory.TenantASlug,
            new LogoutRequest("whatever"));

        loginResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_Endpoint_With_Invalid_Bearer_Returns_401()
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.jwt");

        var resp = await SendWithTenantHostAsync(
            client, HttpMethod.Post, "/api/v1/auth/logout", _factory.TenantASlug,
            new LogoutRequest("whatever"));

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static RegisterRequest NewRegisterRequest(string slug, string email) => new(
        Email: email,
        Password: "CorrectHorseBatteryStaple!",
        FullName: "Test User",
        TenantSlug: slug,
        CompanyName: "Test s.r.o.",
        Ico: null);

    private static async Task<HttpResponseMessage> SendWithTenantHostAsync<T>(
        HttpClient client,
        HttpMethod method,
        string path,
        string tenantSlug,
        T body)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Host = $"{tenantSlug}.az-kotle.cz";
        return await client.SendAsync(request);
    }
}
