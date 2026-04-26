using AzKotle.Application.Abstractions;
using AzKotle.Application.Common;
using AzKotle.Application.Locations;
using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Locations;
using AzKotle.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace AzKotle.Api.Endpoints;

public static class LocationEndpoints
{
    public static IEndpointRouteBuilder MapLocationEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/locations").RequireAuthorization();

        group.MapGet("/", ListAsync).WithName("LocationsList");
        group.MapGet("/{id:guid}", GetAsync).WithName("LocationById");
        group.MapPost("/", CreateAsync).WithName("LocationCreate");
        group.MapPut("/{id:guid}", UpdateAsync).WithName("LocationUpdate");
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("LocationDelete");

        return routes;
    }

    private static async Task<IResult> ListAsync(
        [FromQuery] Guid? customerId,
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        AzKotleDbContext db,
        CancellationToken ct)
    {
        var size = CursorPagination.ClampPageSize(pageSize);
        var query = db.Locations.AsNoTracking().AsQueryable();

        if (customerId.HasValue)
        {
            query = query.Where(l => l.CustomerId == new CustomerId(customerId.Value));
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(l => EF.Functions.ILike(l.Street, pattern)
                || EF.Functions.ILike(l.City, pattern)
                || EF.Functions.ILike(l.Zip, pattern));
        }

        if (CursorPagination.TryDecode(cursor, out var ca, out var cId))
        {
            var cursorLid = new LocationId(cId);
            query = query.Where(l =>
                l.CreatedAt < ca || (l.CreatedAt == ca && l.Id < cursorLid));
        }

        var rows = await query
            .OrderByDescending(l => l.CreatedAt)
            .ThenByDescending(l => l.Id)
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
        return Results.Ok(new PagedResponse<LocationDto>(items, nextCursor));
    }

    private static async Task<IResult> GetAsync(Guid id, AzKotleDbContext db, CancellationToken ct)
    {
        var location = await db.Locations.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == new LocationId(id), ct);
        return location is null ? Results.NotFound() : Results.Ok(ToDto(location));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateLocationRequest request,
        IValidator<CreateLocationRequest> validator,
        AzKotleDbContext db,
        ITenantContext tenantContext,
        TimeProvider time,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var customerExists = await db.Customers.AnyAsync(c => c.Id == new CustomerId(request.CustomerId), ct);
        if (!customerExists)
        {
            return Results.NotFound(new { error = "customer_not_found" });
        }

        var tenantId = tenantContext.Current!.Value;
        var location = Location.Create(tenantId, new CustomerId(request.CustomerId),
            request.Street, request.City, request.Zip, time);
        if (request.GpsLatitude.HasValue && request.GpsLongitude.HasValue)
        {
            location.SetGps(request.GpsLatitude.Value, request.GpsLongitude.Value, time);
        }
        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            location.SetNotes(request.Notes, time);
        }

        db.Locations.Add(location);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/locations/{location.Id.Value}", ToDto(location));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateLocationRequest request,
        IValidator<UpdateLocationRequest> validator,
        AzKotleDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var location = await db.Locations.FirstOrDefaultAsync(l => l.Id == new LocationId(id), ct);
        if (location is null)
        {
            return Results.NotFound();
        }

        location.UpdateAddress(request.Street, request.City, request.Zip, time);
        if (request.GpsLatitude.HasValue && request.GpsLongitude.HasValue)
        {
            location.SetGps(request.GpsLatitude.Value, request.GpsLongitude.Value, time);
        }
        else
        {
            location.ClearGps(time);
        }
        location.SetNotes(request.Notes, time);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(location));
    }

    private static async Task<IResult> DeleteAsync(Guid id, AzKotleDbContext db, CancellationToken ct)
    {
        var location = await db.Locations.FirstOrDefaultAsync(l => l.Id == new LocationId(id), ct);
        if (location is null)
        {
            return Results.NotFound();
        }

        db.Locations.Remove(location);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static LocationDto ToDto(Location l) => new(
        l.Id.Value, l.CustomerId.Value, l.Street, l.City, l.Zip,
        l.GpsLatitude, l.GpsLongitude, l.Notes, l.CreatedAt, l.UpdatedAt);
}
