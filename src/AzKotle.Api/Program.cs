using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
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
using AzKotle.Infrastructure.Persistence;
using AzKotle.Infrastructure.PublicAccess;
using AzKotle.Infrastructure.QrCodes;
using AzKotle.Infrastructure.Storage;
using AzKotle.Infrastructure.Tenants;
using FluentValidation;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using QuestPDF.Infrastructure;
using Serilog;

QuestPDF.Settings.License = LicenseType.Community;

// One-shot migration mode (Compose 'migrator' sidecar). Runs as POSTGRES_USER
// (superuser, DDL-capable) — separate from runtime API which uses azkotle_app
// (NOSUPERUSER NOBYPASSRLS) so RLS actually enforces. Without this split, the
// runtime user couldn't run migrations OR a superuser-runtime would silently
// bypass tenant_isolation. See deploy/postgres/README.md.
if (args.Contains("--apply-migrations"))
{
    return await ApplyMigrationsAsync(args);
}

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
builder.Services.AddScoped<InspectionPhotoService>();
builder.Services.AddScoped<TenantBrandingService>();
builder.Services.AddScoped<TeamService>();
builder.Services.AddScoped<PublicInspectionService>();

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

builder.Services.AddAuthorization(options =>
{
    // OwnerOnly — RequireRole("Owner") nefunguje spolehlivě s JsonWebTokenHandler:
    // token nese krátký claim "role", default RoleClaimType je ClaimTypes.Role,
    // a moderní handler claim type nepřemapuje. Assertion checkuje obojí.
    options.AddPolicy(TeamEndpoints.OwnerOnlyPolicy, policy =>
        policy.RequireAuthenticatedUser()
              .RequireAssertion(ctx =>
                  ctx.User.FindFirst("role")?.Value == "Owner"
                  || ctx.User.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value == "Owner"));
});

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

// Rate limiting na public viewer endpointech (F14 + F17) — chrání před brute-force
// scanovanim 192-bit access_hashů a před spam audit-log entries. Limit je velkorysejší
// než auth (zákazník může klikat opakovaně Stáhnout PDF), ale stále agresivní k bot
// scanům: 30 attempts/IP/min ≈ 0.5 RPS, brute-force horizont na 192 bitů zůstává ∞.
const string PublicAccessRateLimitPolicy = "PublicAccessPolicy";
var publicPermitLimit = builder.Configuration.GetValue("RateLimit:Public:PermitLimit", 30);
var publicWindowSeconds = builder.Configuration.GetValue("RateLimit:Public:WindowSeconds", 60);

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

    // PartitionedRateLimiter.Create<HttpContext, string>: každý unique klíč (IP) má
    // vlastní fixed window, takže útočník na jedné IP nezablokuje legitimní zákazníky
    // za jiným NAT. Pokud RemoteIpAddress chybí (test runner / weird proxy), všichni
    // sdílí "unknown" bucket — bezpečné fail-closed default.
    options.AddPolicy(PublicAccessRateLimitPolicy, context =>
    {
        var key = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = publicPermitLimit,
            Window = TimeSpan.FromSeconds(publicWindowSeconds),
            QueueLimit = 0,
            AutoReplenishment = true,
        });
    });

    // OnRejected běží před tím, než pipeline cokoli zapíše — bezpečně nastavujeme
    // status, hlavičky i body. Aplikuje se globálně na všechny rate-limit policies
    // (auth + public); body je mírné UX vylepšení i pro auth rejections.
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";

        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
        {
            context.HttpContext.Response.Headers.RetryAfter =
                ((int)retryAfter.TotalSeconds).ToString();
        }

        await context.HttpContext.Response.WriteAsync(
            """{"error":"Příliš mnoho požadavků. Zkuste to prosím za minutu."}""",
            ct);
    };
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

app.MapGet("/health/ready", async (
    AzKotleDbContext db,
    IFileStorage storage,
    CancellationToken ct) =>
{
    // Probes both DB and S3 — orchestrators use this to gate traffic. Failure of
    // either fails the whole readiness; checks block individually so the JSON body
    // pinpoints which dependency is down (operator can debug without grep'ing logs).
    string dbStatus;
    try
    {
        await db.Database.ExecuteSqlRawAsync("SELECT 1", ct);
        dbStatus = "ok";
    }
    catch (Exception ex)
    {
        dbStatus = ex.GetType().Name;
    }

    var s3Ok = await storage.HeadBucketAsync(ct);
    var s3Status = s3Ok ? "ok" : "unreachable";

    var ready = dbStatus == "ok" && s3Ok;
    var payload = new
    {
        status = ready ? "ready" : "not_ready",
        checks = new { db = dbStatus, s3 = s3Status },
    };
    return ready
        ? Results.Ok(payload)
        : Results.Json(payload, statusCode: StatusCodes.Status503ServiceUnavailable);
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
app.MapTenantBrandingEndpoints();
app.MapTeamEndpoints();
app.MapCustomerEndpoints();
app.MapLocationEndpoints();
app.MapBoilerEndpoints();
app.MapQrCodeEndpoints();
app.MapInspectionEndpoints();
app.MapPublicInspectionEndpoints();
app.MapDashboardEndpoints();

app.MapMetrics(); // /metrics — chráněno Caddy ACL (jen z VPS internal IP nebo blocked navenek).

app.Run();
return 0;

static async Task<int> ApplyMigrationsAsync(string[] args)
{
    // Stand-alone host (no Kestrel, no auth, no rate limiter) — just config
    // loading and DbContext. Reuses ASPNETCORE_ENVIRONMENT and standard env-var
    // overrides, so the same Compose env-var conventions apply here.
    var hostBuilder = Host.CreateApplicationBuilder(args);
    hostBuilder.Logging.ClearProviders();
    hostBuilder.Logging.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; });

    var connectionString = hostBuilder.Configuration.GetConnectionString("AzKotleDb")
        ?? throw new InvalidOperationException(
            "ConnectionStrings:AzKotleDb is required for --apply-migrations. " +
            "Set ConnectionStrings__AzKotleDb env var (sidecar uses POSTGRES_USER credentials).");

    using var loggerFactory = LoggerFactory.Create(b =>
        b.AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; }));
    var logger = loggerFactory.CreateLogger("Migrator");

    logger.LogInformation("Applying migrations to {ConnectionString}", MaskPassword(connectionString));

    // Bypass DI / TenantContextInterceptor — migrator doesn't need tenant context
    // (DDL is RLS-irrelevant) and the interceptor would just push an empty
    // app.current_tenant_id setting which is harmless but noisy.
    var options = new DbContextOptionsBuilder<AzKotleDbContext>()
        .UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsAssembly(typeof(AzKotleDbContext).Assembly.FullName))
        .Options;

    try
    {
        await using var db = new AzKotleDbContext(options);
        var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
        if (pending.Count == 0)
        {
            logger.LogInformation("Database is up to date — no pending migrations.");
            return 0;
        }
        logger.LogInformation("Applying {Count} migration(s): {Names}",
            pending.Count, string.Join(", ", pending));
        await db.Database.MigrateAsync();
        logger.LogInformation("Migrations applied successfully.");
        return 0;
    }
    catch (Exception ex)
    {
        // Exit code 1 → Compose `condition: service_completed_successfully` halts
        // the rollout; api / app stay un-started, /health/ready returns 503 if
        // anyone bypasses ordering. Operator inspects logs and re-rolls.
        logger.LogCritical(ex, "Migration failed — aborting deploy");
        return 1;
    }

    static string MaskPassword(string connStr) =>
        Regex.Replace(connStr, @"(Password|Pwd)\s*=\s*[^;]+", "$1=***",
            RegexOptions.IgnoreCase, TimeSpan.FromMilliseconds(100));
}

public partial class Program;
