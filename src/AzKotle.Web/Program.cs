using AzKotle.Web.Client.Auth;
using AzKotle.Web.Components;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor.Services;
using Prometheus;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, sp, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
       .ReadFrom.Services(sp)
       .Enrich.FromLogContext()
       .Enrich.WithProperty("Application", "AzKotle.Web")
       .Enrich.WithProperty("Environment", ctx.HostingEnvironment.EnvironmentName)
       .WriteTo.Console();

    var seqUrl = ctx.Configuration["Serilog:Seq:ServerUrl"];
    if (!string.IsNullOrWhiteSpace(seqUrl))
    {
        cfg.WriteTo.Seq(seqUrl, apiKey: ctx.Configuration["Serilog:Seq:ApiKey"]);
    }
});

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    .AddInteractiveWebAssemblyComponents();

builder.Services.AddMudServices();

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<AuthenticationStateProvider, AnonymousServerAuthStateProvider>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseSerilogRequestLogging();
app.UseHttpMetrics();

app.UseAntiforgery();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapMetrics();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(AzKotle.Web.Client._Imports).Assembly);

app.Run();
