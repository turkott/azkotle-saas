using System.Security.Claims;
using AzKotle.Application.Abstractions;
using AzKotle.Application.Tenants.Team;
using AzKotle.Domain.Common;
using AzKotle.Infrastructure.Tenants;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace AzKotle.Api.Endpoints;

public static class TeamEndpoints
{
    public const string OwnerOnlyPolicy = "OwnerOnly";

    public static IEndpointRouteBuilder MapTeamEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/tenant/users")
            .RequireAuthorization(OwnerOnlyPolicy);

        group.MapGet("/", ListAsync).WithName("TeamList");
        group.MapPost("/", CreateAsync).WithName("TeamCreateTechnician");
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("TeamDelete");

        return routes;
    }

    private static async Task<IResult> ListAsync(
        TeamService team,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (tenantContext.Current is not { } tenantId)
        {
            return Results.Unauthorized();
        }

        var users = await team.ListAsync(tenantId, ct);
        return Results.Ok(users);
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateTechnicianRequest request,
        IValidator<CreateTechnicianRequest> validator,
        TeamService team,
        ITenantContext tenantContext,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        if (tenantContext.Current is not { } tenantId)
        {
            return Results.Unauthorized();
        }
        if (!TryGetUserId(user, out var actorId))
        {
            return Results.Unauthorized();
        }

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = NullIfBlank(httpContext.Request.Headers.UserAgent.ToString());

        var result = await team.CreateTechnicianAsync(tenantId, actorId, request, ipAddress, userAgent, ct);
        return result switch
        {
            CreateTechnicianResult.Success ok =>
                Results.Created($"/api/v1/tenant/users/{ok.User.Id}", ok.User),
            CreateTechnicianResult.EmailTaken =>
                Results.Conflict(new { error = "email_taken" }),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        TeamService team,
        ITenantContext tenantContext,
        ClaimsPrincipal user,
        HttpContext httpContext,
        CancellationToken ct)
    {
        if (tenantContext.Current is not { } tenantId)
        {
            return Results.Unauthorized();
        }
        if (!TryGetUserId(user, out var actorId))
        {
            return Results.Unauthorized();
        }

        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        var userAgent = NullIfBlank(httpContext.Request.Headers.UserAgent.ToString());

        var result = await team.DeleteAsync(tenantId, actorId, new UserId(id), ipAddress, userAgent, ct);
        return result switch
        {
            DeleteUserResult.Success => Results.NoContent(),
            DeleteUserResult.NotFound => Results.NotFound(),
            DeleteUserResult.SelfDeleteForbidden => Results.BadRequest(new { error = "self_delete_forbidden" }),
            _ => Results.StatusCode(StatusCodes.Status500InternalServerError),
        };
    }

    private static bool TryGetUserId(ClaimsPrincipal user, out UserId userId)
    {
        userId = UserId.Empty;
        var sub = user.FindFirstValue("sub") ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(sub, out var guid))
        {
            userId = new UserId(guid);
            return true;
        }
        return false;
    }

    private static string? NullIfBlank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}
