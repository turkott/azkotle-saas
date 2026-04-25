using AzKotle.Api.MultiTenancy;
using AzKotle.Application.Abstractions;
using AzKotle.Domain.Common;
using AzKotle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AzKotle.Api.Endpoints;

public static class QrCodeEndpoints
{
    public static IEndpointRouteBuilder MapQrCodeEndpoints(this IEndpointRouteBuilder routes)
    {
        var protectedGroup = routes.MapGroup("/api/v1/boilers/{id:guid}").RequireAuthorization();

        protectedGroup.MapGet("/qr.png", QrPngAsync).WithName("BoilerQrPng");
        protectedGroup.MapGet("/qr-label", QrLabelPdfAsync).WithName("BoilerQrLabelPdf");

        routes.MapGet("/qr/{code}", PublicQrAsync)
            .WithName("PublicQrRedirect")
            .WithMetadata(new AllowAnonymousTenantAttribute())
            .AllowAnonymous();

        return routes;
    }

    private static async Task<IResult> QrPngAsync(
        Guid id,
        AzKotleDbContext db,
        IQrCodeImageRenderer renderer,
        IConfiguration config,
        CancellationToken ct)
    {
        var boiler = await db.Boilers.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == new BoilerId(id), ct);
        if (boiler is null)
        {
            return Results.NotFound();
        }

        var url = BuildPublicQrUrl(config, boiler.QrCode);
        var png = renderer.RenderPng(url, pixelsPerModule: 12);
        return Results.File(png, "image/png", $"{boiler.QrCode}.png");
    }

    private static async Task<IResult> QrLabelPdfAsync(
        Guid id,
        [Microsoft.AspNetCore.Mvc.FromQuery] int? copies,
        AzKotleDbContext db,
        IBoilerLabelPdfRenderer pdf,
        IConfiguration config,
        CancellationToken ct)
    {
        var requested = Math.Clamp(copies ?? 4, 1, 24);

        var boiler = await db.Boilers.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == new BoilerId(id), ct);
        if (boiler is null)
        {
            return Results.NotFound();
        }

        var location = await db.Locations.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == boiler.LocationId, ct);
        var locationLabel = location is null
            ? string.Empty
            : $"{location.Street}, {location.Zip} {location.City}";

        var label = new BoilerLabel(
            QrCode: boiler.QrCode,
            QrTargetUrl: BuildPublicQrUrl(config, boiler.QrCode),
            Manufacturer: boiler.Manufacturer,
            Model: boiler.Model,
            SerialNo: boiler.SerialNo,
            LocationLabel: locationLabel);

        var labels = Enumerable.Repeat(label, requested).ToList();
        var bytes = pdf.RenderA4Sheet(labels);
        return Results.File(bytes, "application/pdf", $"qr-{boiler.QrCode}.pdf");
    }

    private static async Task<IResult> PublicQrAsync(
        string code,
        AzKotleDbContext db,
        IConfiguration config,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var webBase = TrimSlash(config["Web:BaseUrl"] ?? "/");
        var loginPath = $"{webBase}/login?redirect={Uri.EscapeDataString($"/qr/{code}")}";

        if (httpContext.User.Identity?.IsAuthenticated != true)
        {
            return Results.Redirect(loginPath);
        }

        var boiler = await db.Boilers.AsNoTracking()
            .FirstOrDefaultAsync(b => b.QrCode == code, ct);
        if (boiler is null)
        {
            return Results.NotFound(new { error = "qr_not_found" });
        }

        var detailUrl = $"{webBase}/boilers/{boiler.Id.Value}";
        return Results.Redirect(detailUrl);
    }

    private static string BuildPublicQrUrl(IConfiguration config, string qrCode)
    {
        var publicBase = TrimSlash(config["Qr:PublicBaseUrl"] ?? config["Web:BaseUrl"] ?? "https://app.az-kotle.cz");
        return $"{publicBase}/qr/{qrCode}";
    }

    private static string TrimSlash(string value) =>
        value.EndsWith('/') ? value[..^1] : value;
}
