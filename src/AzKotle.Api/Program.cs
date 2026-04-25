using System.Text;
using AzKotle.Api.Endpoints;
using AzKotle.Api.MultiTenancy;
using AzKotle.Application.Abstractions;
using AzKotle.Infrastructure;
using AzKotle.Infrastructure.Auth;
using AzKotle.Infrastructure.External;
using AzKotle.Infrastructure.QrCodes;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("AzKotleDb")
    ?? "Host=localhost;Port=5432;Database=azkotle;Username=postgres;Password=postgres";

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddAzKotleDb(connectionString);
builder.Services.AddAzKotleHttpTenancy();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
builder.Services.AddSingleton<IJwtTokenService, JwtTokenService>();
builder.Services.AddSingleton<IBoilerQrSlugGenerator, BoilerQrSlugGenerator>();
builder.Services.AddSingleton<IQrCodeImageRenderer, QrCoderImageRenderer>();
builder.Services.AddSingleton<IBoilerLabelPdfRenderer, BoilerLabelPdfRenderer>();

builder.Services.AddHttpClient<IAresClient, AresClient>(client =>
{
    client.BaseAddress = new Uri("https://ares.gov.cz/");
    client.Timeout = TimeSpan.FromSeconds(5);
    client.DefaultRequestHeaders.Add("User-Agent", "AzKotle-SaaS/1.0");
});

builder.Services.AddValidatorsFromAssemblyContaining<AzKotle.Application.Auth.Validators.RegisterRequestValidator>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>() ?? new JwtOptions();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidAudience = jwtOptions.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.Secret)),
            ClockSkew = TimeSpan.FromSeconds(30),
        };
    });

builder.Services.AddAuthorization();

const string CorsPolicy = "AzKotleCors";
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        var configured = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? new[] { "http://localhost:5100", "https://localhost:5101" };

        policy.SetIsOriginAllowed(origin =>
            {
                if (string.IsNullOrWhiteSpace(origin))
                    return false;
                if (configured.Contains(origin, StringComparer.OrdinalIgnoreCase))
                    return true;
                try
                {
                    var host = new Uri(origin).Host;
                    return host.EndsWith(".az-kotle.cz", StringComparison.OrdinalIgnoreCase)
                        || host.Equals("az-kotle.cz", StringComparison.OrdinalIgnoreCase)
                        || host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
                }
                catch (UriFormatException)
                {
                    return false;
                }
            })
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseCors(CorsPolicy);

app.UseAuthentication();
app.UseAuthorization();
app.UseTenantResolution();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health")
    .WithMetadata(new AllowAnonymousTenantAttribute())
    .AllowAnonymous();

app.MapGet("/whoami", (ITenantContext tenantContext) =>
        Results.Ok(new { tenantId = tenantContext.Current?.Value }))
    .WithName("WhoAmI")
    .AllowAnonymous();

app.MapAuthEndpoints();
app.MapLookupEndpoints();
app.MapCustomerEndpoints();
app.MapLocationEndpoints();
app.MapBoilerEndpoints();
app.MapQrCodeEndpoints();
app.MapInspectionEndpoints();

app.Run();

public partial class Program;
