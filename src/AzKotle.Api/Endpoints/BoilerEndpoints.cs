using AzKotle.Application.Abstractions;
using AzKotle.Application.Boilers;
using AzKotle.Application.Common;
using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Boilers;
using AzKotle.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace AzKotle.Api.Endpoints;

public static class BoilerEndpoints
{
    private const int QrCollisionRetries = 5;
    private const string UniqueViolationSqlState = "23505";

    public static IEndpointRouteBuilder MapBoilerEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/boilers").RequireAuthorization();

        group.MapGet("/", ListAsync).WithName("BoilersList");
        group.MapGet("/{id:guid}", GetAsync).WithName("BoilerById");
        group.MapPost("/", CreateAsync).WithName("BoilerCreate");
        group.MapPut("/{id:guid}", UpdateAsync).WithName("BoilerUpdate");
        group.MapPost("/{id:guid}/inspections", RecordInspectionAsync).WithName("BoilerRecordInspection");
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("BoilerDelete");

        return routes;
    }

    private static async Task<IResult> ListAsync(
        [FromQuery] Guid? locationId,
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        AzKotleDbContext db,
        CancellationToken ct)
    {
        var size = CursorPagination.ClampPageSize(pageSize);
        var query = db.Boilers.AsNoTracking().AsQueryable();

        if (locationId.HasValue)
        {
            query = query.Where(b => b.LocationId == new LocationId(locationId.Value));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(b => EF.Functions.ILike(b.Manufacturer, pattern)
                || EF.Functions.ILike(b.Model, pattern)
                || EF.Functions.ILike(b.SerialNo, pattern)
                || EF.Functions.ILike(b.QrCode, pattern));
        }

        if (CursorPagination.TryDecode(cursor, out var ca, out var cId))
        {
            var cursorBid = new BoilerId(cId);
            query = query.Where(b =>
                b.CreatedAt < ca || (b.CreatedAt == ca && b.Id < cursorBid));
        }

        var rows = await query
            .OrderByDescending(b => b.CreatedAt)
            .ThenByDescending(b => b.Id)
            .Take(size + 1)
            .ToListAsync(ct);

        string? nextCursor = null;
        if (rows.Count > size)
        {
            var pivot = rows[size - 1];
            nextCursor = CursorPagination.Encode(pivot.CreatedAt, pivot.Id.Value);
            rows.RemoveAt(rows.Count - 1);
        }

        var items = rows.Select(ToDto).ToList();
        return Results.Ok(new PagedResponse<BoilerDto>(items, nextCursor));
    }

    private static async Task<IResult> GetAsync(Guid id, AzKotleDbContext db, CancellationToken ct)
    {
        var boiler = await db.Boilers.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == new BoilerId(id), ct);
        return boiler is null ? Results.NotFound() : Results.Ok(ToDto(boiler));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateBoilerRequest request,
        IValidator<CreateBoilerRequest> validator,
        AzKotleDbContext db,
        ITenantContext tenantContext,
        IBoilerQrSlugGenerator qr,
        TimeProvider time,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var locationExists = await db.Locations.AnyAsync(l => l.Id == new LocationId(request.LocationId), ct);
        if (!locationExists)
        {
            return Results.NotFound(new { error = "location_not_found" });
        }

        var tenantId = tenantContext.Current!.Value;

        for (var attempt = 0; attempt < QrCollisionRetries; attempt++)
        {
            var qrCode = qr.Generate();
            var boiler = Boiler.Register(
                tenantId,
                new LocationId(request.LocationId),
                qrCode,
                request.Manufacturer,
                request.Model,
                request.SerialNo,
                request.OutputKw,
                request.FuelType,
                request.InstalledAt,
                time);

            if (!string.IsNullOrWhiteSpace(request.Notes))
            {
                boiler.SetNotes(request.Notes, time);
            }

            db.Boilers.Add(boiler);
            try
            {
                await db.SaveChangesAsync(ct);
                return Results.Created($"/api/v1/boilers/{boiler.Id.Value}", ToDto(boiler));
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                db.Entry(boiler).State = EntityState.Detached;
                if (attempt == QrCollisionRetries - 1)
                {
                    return Results.Json(new { error = "qr_generation_failed" },
                        statusCode: StatusCodes.Status503ServiceUnavailable);
                }
            }
        }

        return Results.Json(new { error = "qr_generation_failed" },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateBoilerRequest request,
        IValidator<UpdateBoilerRequest> validator,
        AzKotleDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var boiler = await db.Boilers.FirstOrDefaultAsync(b => b.Id == new BoilerId(id), ct);
        if (boiler is null)
        {
            return Results.NotFound();
        }

        boiler.UpdateSpecs(request.Manufacturer, request.Model, request.SerialNo,
            request.OutputKw, request.FuelType, time);
        boiler.SetNotes(request.Notes, time);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(boiler));
    }

    private static async Task<IResult> RecordInspectionAsync(
        Guid id,
        [FromBody] RecordInspectionRequest request,
        IValidator<RecordInspectionRequest> validator,
        AzKotleDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var boiler = await db.Boilers.FirstOrDefaultAsync(b => b.Id == new BoilerId(id), ct);
        if (boiler is null)
        {
            return Results.NotFound();
        }

        try
        {
            boiler.RecordInspection(request.PerformedAt, request.NextDueAt, time);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = "invalid_inspection", detail = ex.Message });
        }

        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(boiler));
    }

    private static async Task<IResult> DeleteAsync(Guid id, AzKotleDbContext db, CancellationToken ct)
    {
        var boiler = await db.Boilers.FirstOrDefaultAsync(b => b.Id == new BoilerId(id), ct);
        if (boiler is null)
        {
            return Results.NotFound();
        }

        db.Boilers.Remove(boiler);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pg && pg.SqlState == UniqueViolationSqlState;

    private static BoilerDto ToDto(Boiler b) => new(
        b.Id.Value, b.LocationId.Value, b.QrCode,
        b.Manufacturer, b.Model, b.SerialNo, b.OutputKw, b.FuelType,
        b.InstalledAt, b.LastInspectionAt, b.NextInspectionDue, b.Notes,
        b.CreatedAt, b.UpdatedAt);
}
