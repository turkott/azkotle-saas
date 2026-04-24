using AzKotle.Api.MultiTenancy;
using AzKotle.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

var connectionString = builder.Configuration.GetConnectionString("AzKotleDb")
    ?? "Host=localhost;Port=5432;Database=azkotle;Username=postgres;Password=postgres";

builder.Services.AddAzKotleDb(connectionString);
builder.Services.AddAzKotleHttpTenancy();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseTenantResolution();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health")
    .WithMetadata(new AllowAnonymousTenantAttribute());

app.MapGet("/whoami", (AzKotle.Application.Abstractions.ITenantContext tenantContext) =>
        Results.Ok(new { tenantId = tenantContext.Current?.Value }))
    .WithName("WhoAmI");

app.Run();

public partial class Program;
