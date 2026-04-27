using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AzKotle.Api.IntegrationTests.MultiTenancy;
using AzKotle.Application.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AzKotle.Api.IntegrationTests.Auth;

public sealed class AuthenticationTests : IClassFixture<AzKotleApiFactory>
{
    private const string RefreshCookieName = "azkotle_refresh";

    private readonly AzKotleApiFactory _factory;

    public AuthenticationTests(AzKotleApiFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Register_Login_Refresh_Happy_Path()
    {
        using var client = CreateClient();
        var registerRequest = NewRegisterRequest("acme-happy", "acme@example.com");

        var registerResp = await client.PostAsJsonAsync("/api/v1/auth/register", registerRequest);
        registerResp.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertRefreshCookieSet(registerResp);
        var registered = await registerResp.Content.ReadFromJsonAsync<AuthResponse>();
        registered.Should().NotBeNull();
        registered!.AccessToken.Should().NotBeNullOrEmpty();

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", registered.AccessToken);
        var whoamiResp = await client.GetAsync("/whoami");
        whoamiResp.StatusCode.Should().Be(HttpStatusCode.OK);
        client.DefaultRequestHeaders.Authorization = null;

        var loginResp = await SendAsync(client, HttpMethod.Post, "/api/v1/auth/login",
            registerRequest.TenantSlug,
            body: new LoginRequest(registerRequest.Email, registerRequest.Password));
        loginResp.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertRefreshCookieSet(loginResp);
        var loginToken = ExtractCookieValue(loginResp, RefreshCookieName);

        var refreshResp = await SendAsync(client, HttpMethod.Post, "/api/v1/auth/refresh",
            registerRequest.TenantSlug,
            body: new RefreshRequest(registerRequest.TenantSlug),
            cookie: loginToken);
        refreshResp.StatusCode.Should().Be(HttpStatusCode.OK);
        AssertRefreshCookieSet(refreshResp);
        var refreshed = await refreshResp.Content.ReadFromJsonAsync<AuthResponse>();
        refreshed!.AccessToken.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Refresh_cookie_has_HttpOnly_Secure_SameSite_Strict_and_correct_path()
    {
        using var client = CreateClient();
        var register = NewRegisterRequest("acme-cookie-attrs", "cookie-attrs@example.com");

        var resp = await client.PostAsJsonAsync("/api/v1/auth/register", register);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var setCookie = resp.Headers.GetValues("Set-Cookie").FirstOrDefault(h => h.StartsWith($"{RefreshCookieName}=", StringComparison.Ordinal));
        setCookie.Should().NotBeNull("register response musí obsahovat Set-Cookie pro refresh token");
        setCookie!.ToLowerInvariant().Should().Contain("httponly", "cookie musí být HttpOnly — JS k ní nesmí mít přístup");
        setCookie.ToLowerInvariant().Should().Contain("secure", "cookie musí mít Secure flag");
        setCookie.ToLowerInvariant().Should().Contain("samesite=strict", "cookie musí být SameSite=Strict");
        setCookie.ToLowerInvariant().Should().Contain("path=/api/v1/auth", "cookie path musí být omezena na /api/v1/auth (refresh + logout)");
        setCookie.ToLowerInvariant().Should().NotContain("domain=", "Domain unset — defaultuje na request host (api.az-kotle.cz only), ne wildcard pod .az-kotle.cz");
    }

    [Fact]
    public async Task Refresh_without_cookie_returns_401()
    {
        using var client = CreateClient();
        var refreshResp = await SendAsync(client, HttpMethod.Post, "/api/v1/auth/refresh",
            tenantSlug: "any-tenant",
            body: new RefreshRequest("any-tenant"));

        refreshResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Register_With_Duplicate_Email_Returns_409()
    {
        using var client = CreateClient();
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
        using var client = CreateClient();
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
        using var client = CreateClient();
        var request = NewRegisterRequest("wrong-pwd", "wrong@example.com");
        await client.PostAsJsonAsync("/api/v1/auth/register", request);

        var loginResp = await SendAsync(client, HttpMethod.Post, "/api/v1/auth/login",
            request.TenantSlug,
            body: new LoginRequest(request.Email, "WrongPassword123!"));

        loginResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Refresh_token_reuse_revokes_entire_chain()
    {
        using var client = CreateClient();
        var request = NewRegisterRequest("chain", "chain@example.com");
        var registerResp = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        registerResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokenA = ExtractCookieValue(registerResp, RefreshCookieName);
        tokenA.Should().NotBeNullOrEmpty();

        // Legitimate rotation: token A → token B (A revoked, B aktivní).
        var rotateResp = await SendAsync(client, HttpMethod.Post, "/api/v1/auth/refresh",
            request.TenantSlug,
            body: new RefreshRequest(request.TenantSlug),
            cookie: tokenA);
        rotateResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var tokenB = ExtractCookieValue(rotateResp, RefreshCookieName);
        tokenB.Should().NotBeNullOrEmpty().And.NotBe(tokenA);

        // Útočník použije ukradený token A — reuse detection musí spustit chain revocation.
        var reuseResp = await SendAsync(client, HttpMethod.Post, "/api/v1/auth/refresh",
            request.TenantSlug,
            body: new RefreshRequest(request.TenantSlug),
            cookie: tokenA);
        reuseResp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "reused token A musí být odmítnut");

        // Po reuse i legitimní následník (token B) musí být zneplatněn.
        var legitimateRetry = await SendAsync(client, HttpMethod.Post, "/api/v1/auth/refresh",
            request.TenantSlug,
            body: new RefreshRequest(request.TenantSlug),
            cookie: tokenB);
        legitimateRetry.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            because: "po reuse detection musí být i token B (validní následník) zneplatněn");
    }

    [Fact]
    public async Task Logout_clears_refresh_cookie_and_subsequent_refresh_fails()
    {
        using var client = CreateClient();
        var request = NewRegisterRequest("logout-flow", "logout@example.com");
        var registerResp = await client.PostAsJsonAsync("/api/v1/auth/register", request);
        var tokenBeforeLogout = ExtractCookieValue(registerResp, RefreshCookieName);

        // Logout je AllowAnonymous — cookie sama prokazuje identitu (HttpOnly + SameSite=Strict).
        var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/auth/logout");
        logoutReq.Headers.Host = $"{request.TenantSlug}.az-kotle.cz";
        logoutReq.Headers.Add("Cookie", $"{RefreshCookieName}={tokenBeforeLogout}");
        var logoutResp = await client.SendAsync(logoutReq);
        logoutResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var clearHeader = logoutResp.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(h => h.StartsWith($"{RefreshCookieName}=", StringComparison.Ordinal));
        clearHeader.Should().NotBeNull();
        clearHeader!.ToLowerInvariant().Should().MatchRegex(@"max-age=0\b", "logout musí browseru říct cookie smazat");

        // Refresh se starým (revoked logoutem) tokenem musí selhat.
        var refreshAfterLogout = await SendAsync(client, HttpMethod.Post, "/api/v1/auth/refresh",
            request.TenantSlug,
            body: new RefreshRequest(request.TenantSlug),
            cookie: tokenBeforeLogout);
        refreshAfterLogout.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Logout_without_cookie_succeeds_silently_with_clear_header()
    {
        // UX: uživatel s expirovaným / chybějícím refresh tokenem se musí umět odhlásit
        // (cookie i tak smazána, lokální session vyčištěna).
        using var client = CreateClient();
        var resp = await client.PostAsync("/api/v1/auth/logout", content: null);

        resp.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var clearHeader = resp.Headers.GetValues("Set-Cookie")
            .FirstOrDefault(h => h.StartsWith($"{RefreshCookieName}=", StringComparison.Ordinal));
        clearHeader.Should().NotBeNull("logout vrací clear cookie i bez vstupní cookie");
    }

    [Fact]
    public async Task Protected_Endpoint_Without_Bearer_Returns_401()
    {
        // Logout už NENÍ Bearer-protected (cookie sama prokazuje identitu),
        // takže testujeme proti běžnému CRUD endpointu.
        using var client = CreateClient();
        var resp = await client.GetAsync("/api/v1/customers");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Protected_Endpoint_With_Invalid_Bearer_Returns_401()
    {
        using var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.jwt");

        var resp = await client.GetAsync("/api/v1/customers");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Klient bez automatické cookie machinery. Refresh token v testech předáváme
    /// explicitně přes Cookie hlavičku, abychom se vyhnuli .NET CookieContainer
    /// nuancím se SameSite=Strict / Secure flag handling. Browser (Blazor WASM)
    /// si cookie spravuje sám.
    /// </summary>
    private HttpClient CreateClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = false,
            AllowAutoRedirect = false,
        });

    private static RegisterRequest NewRegisterRequest(string slug, string email) => new(
        Email: email,
        Password: "CorrectHorseBatteryStaple!",
        FullName: "Test User",
        TenantSlug: slug,
        CompanyName: "Test s.r.o.",
        Ico: null);

    private static async Task<HttpResponseMessage> SendAsync<T>(
        HttpClient client,
        HttpMethod method,
        string path,
        string? tenantSlug,
        T body,
        string? cookie = null)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Content = JsonContent.Create(body),
        };
        if (!string.IsNullOrWhiteSpace(tenantSlug))
        {
            request.Headers.Host = $"{tenantSlug}.az-kotle.cz";
        }
        if (!string.IsNullOrEmpty(cookie))
        {
            request.Headers.Add("Cookie", $"{RefreshCookieName}={cookie}");
        }
        return await client.SendAsync(request);
    }

    private static void AssertRefreshCookieSet(HttpResponseMessage response)
    {
        var setCookie = response.Headers.TryGetValues("Set-Cookie", out var values)
            ? values.FirstOrDefault(h => h.StartsWith($"{RefreshCookieName}=", StringComparison.Ordinal))
            : null;
        setCookie.Should().NotBeNull($"response musí obsahovat Set-Cookie {RefreshCookieName}");
        setCookie.Should().NotMatchRegex($@"^{RefreshCookieName}=;",
            "Set-Cookie nesmí být clear (prázdná hodnota) na úspěšné autentikaci");
    }

    private static string ExtractCookieValue(HttpResponseMessage response, string cookieName)
    {
        var header = response.Headers.GetValues("Set-Cookie")
            .First(h => h.StartsWith($"{cookieName}=", StringComparison.Ordinal));
        var afterEquals = header.Substring(cookieName.Length + 1);
        var semicolon = afterEquals.IndexOf(';');
        return semicolon < 0 ? afterEquals : afterEquals.Substring(0, semicolon);
    }
}
