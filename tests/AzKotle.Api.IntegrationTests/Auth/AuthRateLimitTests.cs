using System.Net;
using System.Net.Http.Json;
using AzKotle.Api.IntegrationTests.MultiTenancy;
using AzKotle.Application.Auth;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;

namespace AzKotle.Api.IntegrationTests.Auth;

public sealed class AuthRateLimitTests : IClassFixture<AuthRateLimitTests.LowLimitFactory>
{
    private readonly LowLimitFactory _factory;

    public AuthRateLimitTests(LowLimitFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Login_returns_429_after_permit_limit_exceeded()
    {
        using var client = _factory.CreateClient();
        var bogus = new LoginRequest("nonexistent@example.com", "WrongPassword!");

        // První 2 requesty projdou až do handleru a vrátí 401 (invalid_credentials).
        for (var i = 0; i < 2; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/v1/auth/login", bogus);
            resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
                because: $"request #{i + 1} musí projít rate limiterem (limit=2)");
        }

        // 3. request musí být odmítnut limitérem PŘED tím, než handler spustí Argon2id.
        var blocked = await client.PostAsJsonAsync("/api/v1/auth/login", bogus);
        blocked.StatusCode.Should().Be(HttpStatusCode.TooManyRequests,
            because: "rate limit policy 'auth' s PermitLimit=2 musí 3. request odmítnout 429");
    }

    /// <summary>
    /// Test factory s nízkým rate limitem (2/minuta) — izolovaná instance,
    /// nesdílí limiter state s ostatními test classes díky vlastnímu IClassFixture.
    /// </summary>
    public sealed class LowLimitFactory : AzKotleApiFactory
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            builder.UseSetting("RateLimit:Auth:PermitLimit", "2");
            builder.UseSetting("RateLimit:Auth:WindowSeconds", "60");
        }
    }
}
