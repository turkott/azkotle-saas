using System.Security.Claims;
using AzKotle.Application.Abstractions;
using AzKotle.Application.Common;
using AzKotle.Application.Inspections;
using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Inspections;
using AzKotle.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace AzKotle.Api.Endpoints;

public static class InspectionEndpoints
{
    public static IEndpointRouteBuilder MapInspectionEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/inspections").RequireAuthorization();

        group.MapGet("/", ListAsync).WithName("InspectionsList");
        group.MapGet("/{id:guid}", GetAsync).WithName("InspectionById");
        group.MapPost("/", CreateAsync).WithName("InspectionCreate");
        group.MapPut("/{id:guid}/draft", UpdateDraftAsync).WithName("InspectionUpdateDraft");

        return routes;
    }

    private static async Task<IResult> ListAsync(
        [FromQuery] Guid? boilerId,
        [FromQuery] InspectionStatus? status,
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        AzKotleDbContext db,
        CancellationToken ct)
    {
        var size = CursorPagination.ClampPageSize(pageSize);
        var query = db.Inspections.AsNoTracking().AsQueryable();

        if (boilerId.HasValue)
        {
            query = query.Where(i => i.BoilerId == new BoilerId(boilerId.Value));
        }
        if (status.HasValue)
        {
            query = query.Where(i => i.Status == status.Value);
        }
        if (CursorPagination.TryDecode(cursor, out var ca))
        {
            query = query.Where(i => i.CreatedAt < ca);
        }

        var rows = await query
            .OrderByDescending(i => i.CreatedAt)
            .Take(size + 1)
            .ToListAsync(ct);

        string? nextCursor = null;
        if (rows.Count > size)
        {
            var pivot = rows[size - 1];
            nextCursor = CursorPagination.Encode(pivot.CreatedAt);
            rows.RemoveAt(rows.Count - 1);
        }

        var items = rows.Select(ToDto).ToList();
        return Results.Ok(new PagedResponse<InspectionDto>(items, nextCursor));
    }

    private static async Task<IResult> GetAsync(Guid id, AzKotleDbContext db, CancellationToken ct)
    {
        var inspection = await db.Inspections.AsNoTracking()
            .FirstOrDefaultAsync(i => i.Id == new InspectionId(id), ct);
        return inspection is null ? Results.NotFound() : Results.Ok(ToDto(inspection));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateInspectionRequest request,
        IValidator<CreateInspectionRequest> validator,
        AzKotleDbContext db,
        ITenantContext tenantContext,
        ClaimsPrincipal user,
        TimeProvider time,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var boilerExists = await db.Boilers.AnyAsync(b => b.Id == new BoilerId(request.BoilerId), ct);
        if (!boilerExists)
        {
            return Results.NotFound(new { error = "boiler_not_found" });
        }

        if (!TryGetUserId(user, out var technicianId))
        {
            return Results.Unauthorized();
        }

        var tenantId = tenantContext.Current!.Value;
        var inspection = Inspection.Draft(
            tenantId,
            new BoilerId(request.BoilerId),
            technicianId,
            request.Type,
            DateTime.SpecifyKind(request.PerformedAt, DateTimeKind.Utc),
            time);

        db.Inspections.Add(inspection);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/inspections/{inspection.Id.Value}", ToDto(inspection));
    }

    private static async Task<IResult> UpdateDraftAsync(
        Guid id,
        [FromBody] UpdateInspectionDraftRequest request,
        IValidator<UpdateInspectionDraftRequest> validator,
        AzKotleDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var inspection = await db.Inspections.FirstOrDefaultAsync(i => i.Id == new InspectionId(id), ct);
        if (inspection is null)
        {
            return Results.NotFound();
        }

        try
        {
            inspection.UpdateFormData(request.FormDataJson, time);
            inspection.UpdateNarrative(request.Findings, request.Recommendations, request.NextDueAt, time);
        }
        catch (InvalidOperationException ex)
        {
            return Results.BadRequest(new { error = "invalid_state", detail = ex.Message });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = "invalid_input", detail = ex.Message });
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(inspection));
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

    private static InspectionDto ToDto(Inspection i) => new(
        i.Id.Value, i.BoilerId.Value, i.TechnicianId.Value,
        i.Type, i.PerformedAt, i.Status,
        i.FormDataJson, i.Findings, i.Recommendations, i.NextDueAt,
        i.PdfB2Key, i.PdfSha256, i.SignedAt,
        i.CreatedAt, i.UpdatedAt);
}
