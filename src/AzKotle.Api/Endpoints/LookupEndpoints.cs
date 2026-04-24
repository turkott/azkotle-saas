using AzKotle.Api.MultiTenancy;
using AzKotle.Application.Abstractions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace AzKotle.Api.Endpoints;

public static class LookupEndpoints
{
    public static IEndpointRouteBuilder MapLookupEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/lookup");

        group.MapGet("/ares/{ico}", AresAsync)
            .WithName("LookupAres")
            .WithMetadata(new AllowAnonymousTenantAttribute())
            .AllowAnonymous();

        return routes;
    }

    private static async Task<IResult> AresAsync(
        string ico,
        IAresClient ares,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(ico) || ico.Length != 8 || !ico.All(char.IsDigit))
        {
            return Results.BadRequest(new { error = "invalid_ico" });
        }

        try
        {
            var company = await ares.LookupByIcoAsync(ico, ct);
            return company is null
                ? Results.NotFound(new { error = "ico_not_found" })
                : Results.Ok(company);
        }
        catch (HttpRequestException)
        {
            return Results.Json(new { error = "ares_unavailable" }, statusCode: StatusCodes.Status502BadGateway);
        }
        catch (TaskCanceledException)
        {
            return Results.Json(new { error = "ares_timeout" }, statusCode: StatusCodes.Status504GatewayTimeout);
        }
    }
}
