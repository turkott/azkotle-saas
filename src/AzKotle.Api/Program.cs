using System.Text;
using System.Threading.RateLimiting;
using AzKotle.Api.Endpoints;
using AzKotle.Api.MultiTenancy;
using AzKotle.Application.Abstractions;
using AzKotle.Application.Inspections;
using AzKotle.Infrastructure;
using AzKotle.Infrastructure.Auth;
using AzKotle.Infrastructure.External;
using AzKotle.Infrastructure.Inspections;
using AzKotle.Infrastructure.Pdf;
using AzKotle.Infrastructure.QrCodes;
using AzKotle.Infrastructure.Storage;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using QuestPDF.Infrastructure;
using Serilog;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, sp, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .ReadFrom.Services(sp)
       .Enrich.FromLogContext()
       .Enrich.WithProperty("Application", "AzKotle.Api")
       .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName)
       .WriteTo.Console();

    var seqUrl = ctx.Configuration["Serilog:Seq:ServerUrl"];
    if (!string.IsNullOrWhiteSpace(seqUrl))
    {
        cfg.WriteTo.Seq(seqUrl, apiKey: ctx.Configuration["Serilog:Seq:ApiKey"]);
    }
});

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
builder.Services.AddSingleton<IInspectionReportPdfRenderer, InspectionReportPdfRenderer>();
builder.Services.AddSingleton<IInspectionTemplateProvider, EmbeddedInspectionTemplateProvider>();
builder.Services.AddSingleton<FormSectionMapper>();
builder.Services.AddScoped<InspectionReportBuilder>();

builder.Services.Configure<StorageOptions>(builder.Configuration.GetSection(StorageOptions.SectionName));
builder.Services.AddSingleton<IFileStorage, S3FileStorage>();
builder.Services.AddScoped<InspectionSignService>();
builder.Services.AddScoped<InspectionPdfDownloadService>();

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

// ForwardedHeaders: za Caddy reverse proxy (compose: api je jen na internal síti,
// jediná cesta dovnitř je přes Caddy). KnownProxies/KnownNetworks vyčištěny —
// věříme jakémukoli příchozímu X-Forwarded-For, protože síťová izolace zajišťuje,
// že request mohl přijít jen přes Caddy. Bez toho by RemoteIpAddress byla
// interní IP Caddy kontejneru a rate limiter by všem útočníkům dal jeden bucket.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

// Rate limiting na auth endpointech — chrání Argon2id (~150 ms / 64 MB peak per verify)
// před DoS. Limit konfigurovatelný (testy si přepíší přes UseSetting).
const string AuthRateLimitPolicy = "auth";
var authPermitLimit = builder.Configuration.GetValue("RateLimit:Auth:PermitLimit", 5);
var authWindowSeconds = builder.Configuration.GetValue("RateLimit:Auth:WindowSeconds", 60);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy(AuthRateLimitPolicy, context =>
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = authPermitLimit,
            Window = TimeSpan.FromSeconds(authWindowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    });
});

const string CorsPolicy = "AzKotleCors";
var isDevelopment = builder.Environment.IsDevelopment();
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        var configured = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
            ?? Array.Empty<string>();

        policy.SetIsOriginAllowed(origin =>
            {
                if (string.IsNullOrWhiteSpace(origin))
                    return false;
                if (configured.Contains(origin, StringComparer.OrdinalIgnoreCase))
                    return true;
                if (!isDevelopment)
                    return false;

                // Development-only: povolit localhost / 127.0.0.1 na libovolném portu/schématu.
                try
                {
                    var host = new Uri(origin).Host;
                    return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
                        || host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase);
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

// MUSÍ být první middleware — nastaví Request.Scheme a RemoteIpAddress podle
// X-Forwarded-* hlaviček od Caddy. Bez toho HttpsRedirection redirectuje
// donekonečna a rate limiter partitionuje na interní IP Caddy.
app.UseForwardedHeaders();

app.UseHttpsRedirection();

app.Use(async (ctx, next) =>
{
    var correlationId = ctx.Request.Headers["X-Correlation-ID"].FirstOrDefault();
    if (string.IsNullOrWhiteSpace(correlationId) || correlationId.Length > 64)
    {
        correlationId = Guid.NewGuid().ToString("N")[..16];
    }
    ctx.Response.Headers["X-Correlation-ID"] = correlationId;
    using (Serilog.Context.LogContext.PushProperty("CorrelationId", correlationId))
    {
        await next();
    }
});

app.UseSerilogRequestLogging();
app.UseHttpMetrics();

app.UseCors(CorsPolicy);

// Před UseAuthentication — chceme rate limit aplikovat dřív než Argon2id
// běží v LoginAsync handleru. Routing už je vyřešený (Minimal API ho přidává
// implicitně), takže rate limiter vidí endpoint metadata a zná policy.
app.UseRateLimiter();

app.UseAuthentication();
app.UseAuthorization();
app.UseTenantResolution();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health")
    .WithMetadata(new AllowAnonymousTenantAttribute())
    .AllowAnonymous();

app.MapGet("/health/ready", async (AzKotle.Infrastructure.Persistence.AzKotleDbContext db, CancellationToken ct) =>
{
    try
    {
        await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
        return Results.Ok(new { status = "ready" });
    }
    catch (Exception ex)
    {
        return Results.Json(new { status = "not_ready", error = ex.GetType().Name }, statusCode: 503);
    }
})
    .WithName("HealthReady")
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

app.MapMetrics(); // /metrics — chráněno Caddy ACL (jen z VPS internal IP nebo blocked navenek).

app.Run();

public partial class Program;
