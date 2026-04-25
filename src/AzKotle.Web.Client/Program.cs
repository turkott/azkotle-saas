using AzKotle.Web.Client.Api;
using AzKotle.Web.Client.Auth;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMudServices();

builder.Services.AddScoped<BrowserStorage>();
builder.Services.AddScoped<AuthSession>();
builder.Services.AddScoped<JwtAuthHandler>();
builder.Services.AddScoped<AuthenticationStateProvider, JwtAuthenticationStateProvider>();
builder.Services.AddAuthorizationCore();

var apiBaseAddress = builder.Configuration["Api:BaseAddress"]
    ?? "http://localhost:5080";

builder.Services.AddHttpClient<AuthApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseAddress);
})
.AddHttpMessageHandler<JwtAuthHandler>();

builder.Services.AddHttpClient<LookupApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBaseAddress);
});

builder.Services.AddHttpClient<CustomersApiClient>(client => client.BaseAddress = new Uri(apiBaseAddress))
    .AddHttpMessageHandler<JwtAuthHandler>();
builder.Services.AddHttpClient<LocationsApiClient>(client => client.BaseAddress = new Uri(apiBaseAddress))
    .AddHttpMessageHandler<JwtAuthHandler>();
builder.Services.AddHttpClient<BoilersApiClient>(client => client.BaseAddress = new Uri(apiBaseAddress))
    .AddHttpMessageHandler<JwtAuthHandler>();

await builder.Build().RunAsync();
