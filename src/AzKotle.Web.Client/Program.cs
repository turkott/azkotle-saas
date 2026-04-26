using AzKotle.Web.Client.Api;
using AzKotle.Web.Client.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMudServices();

builder.Services.AddScoped<TenantSlugStorage>();
builder.Services.AddScoped<AuthSession>();
builder.Services.AddTransient<BrowserCredentialsHandler>();
builder.Services.AddTransient<JwtAuthHandler>();
builder.Services.AddScoped<AuthenticationStateProvider, JwtAuthenticationStateProvider>();
builder.Services.AddAuthorizationCore();

var apiBaseAddress = builder.Configuration["Api:BaseAddress"]
    ?? "http://localhost:5080";

// AuthApiClient — anonymní auth endpointy, jen credentials.include (cookies).
// Záměrně NEPoužívá JwtAuthHandler:
//   (a) auth endpointy nejsou Bearer-protected (login/register/refresh/logout),
//   (b) tím se rozkroutí circular dep: JwtAuthHandler→AuthApiClient→JwtAuthHandler.
builder.Services.AddHttpClient<AuthApiClient>(client => client.BaseAddress = new Uri(apiBaseAddress))
    .AddHttpMessageHandler<BrowserCredentialsHandler>();

builder.Services.AddHttpClient<LookupApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseAddress);
});

// CRUD klienti — chain: BrowserCredentialsHandler (credentials.include) → JwtAuthHandler
// (Bearer + 401 retry s refresh). Order záměrný: outer credentials nastaví flag na
// request.Options, persistuje napříč JwtAuthHandler.base.SendAsync retry.
builder.Services.AddHttpClient<CustomersApiClient>(client => client.BaseAddress = new Uri(apiBaseAddress))
    .AddHttpMessageHandler<BrowserCredentialsHandler>()
    .AddHttpMessageHandler<JwtAuthHandler>();
builder.Services.AddHttpClient<LocationsApiClient>(client => client.BaseAddress = new Uri(apiBaseAddress))
    .AddHttpMessageHandler<BrowserCredentialsHandler>()
    .AddHttpMessageHandler<JwtAuthHandler>();
builder.Services.AddHttpClient<BoilersApiClient>(client => client.BaseAddress = new Uri(apiBaseAddress))
    .AddHttpMessageHandler<BrowserCredentialsHandler>()
    .AddHttpMessageHandler<JwtAuthHandler>();
builder.Services.AddHttpClient<InspectionsApiClient>(client => client.BaseAddress = new Uri(apiBaseAddress))
    .AddHttpMessageHandler<BrowserCredentialsHandler>()
    .AddHttpMessageHandler<JwtAuthHandler>();

builder.Services.AddHttpClient<InspectionSchemaClient>(client =>
{
    client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress);
});

var host = builder.Build();

// Pre-render: pokus o navázání session přes přeživší refresh cookie.
// V případě úspěchu UI vyrenderuje rovnou s authentikovaným stavem (žádný flicker
// anonymous → authenticated). V případě selhání (žádná cookie / expired / network)
// pokračujeme jako anonymní.
{
    var authClient = host.Services.GetRequiredService<AuthApiClient>();
    var slugStorage = host.Services.GetRequiredService<TenantSlugStorage>();
    string? slug = null;
    try
    {
        slug = await slugStorage.GetAsync();
    }
    catch
    {
        // localStorage nedostupný (privátní mod / starý browser) — slug zůstane null,
        // refresh stejně může projít přes subdoménový fallback (kdyby existoval).
    }

    try
    {
        await authClient.SilentRefreshAsync(slug);
    }
    catch
    {
        // SilentRefreshAsync sám catchne většinu — toto je jen final safety net.
    }
}

await host.RunAsync();
