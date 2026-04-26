using AzKotle.Application.Abstractions;
using AzKotle.Application.Common;
using AzKotle.Application.Customers;
using AzKotle.Domain.Common;
using AzKotle.Domain.Entities.Customers;
using AzKotle.Infrastructure.Persistence;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace AzKotle.Api.Endpoints;

public static class CustomerEndpoints
{
    public static IEndpointRouteBuilder MapCustomerEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/customers").RequireAuthorization();

        group.MapGet("/", ListAsync).WithName("CustomersList");
        group.MapGet("/{id:guid}", GetAsync).WithName("CustomerById");
        group.MapPost("/", CreateAsync).WithName("CustomerCreate");
        group.MapPut("/{id:guid}", UpdateAsync).WithName("CustomerUpdate");
        group.MapDelete("/{id:guid}", DeleteAsync).WithName("CustomerDelete");

        return routes;
    }

    private static async Task<IResult> ListAsync(
        [FromQuery] string? cursor,
        [FromQuery] int? pageSize,
        [FromQuery] string? search,
        [FromQuery] CustomerType? type,
        AzKotleDbContext db,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        var size = CursorPagination.ClampPageSize(pageSize);
        var query = db.Customers.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(c => EF.Functions.ILike(c.Name, pattern)
                || (c.Email != null && EF.Functions.ILike(c.Email, pattern))
                || (c.Ico != null && EF.Functions.ILike(c.Ico, pattern)));
        }

        if (type.HasValue)
        {
            query = query.Where(c => c.Type == type.Value);
        }

        if (CursorPagination.TryDecode(cursor, out var ca, out var cId))
        {
            var cursorCid = new CustomerId(cId);
            query = query.Where(c =>
                c.CreatedAt < ca || (c.CreatedAt == ca && c.Id < cursorCid));
        }

        var rows = await query
            .OrderByDescending(c => c.CreatedAt)
            .ThenByDescending(c => c.Id)
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
        return Results.Ok(new PagedResponse<CustomerDto>(items, nextCursor));
    }

    private static async Task<IResult> GetAsync(
        Guid id,
        AzKotleDbContext db,
        CancellationToken ct)
    {
        var customer = await db.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == new CustomerId(id), ct);
        return customer is null ? Results.NotFound() : Results.Ok(ToDto(customer));
    }

    private static async Task<IResult> CreateAsync(
        [FromBody] CreateCustomerRequest request,
        IValidator<CreateCustomerRequest> validator,
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

        var tenantId = tenantContext.Current!.Value;
        var customer = Customer.Create(tenantId, request.Type, request.Name, time);
        if (!string.IsNullOrWhiteSpace(request.Ico))
        {
            customer.SetIco(request.Ico, time);
        }
        if (!string.IsNullOrWhiteSpace(request.Email) || !string.IsNullOrWhiteSpace(request.Phone))
        {
            customer.SetContactInfo(request.Email, request.Phone, time);
        }
        if (!string.IsNullOrWhiteSpace(request.Notes))
        {
            customer.SetNotes(request.Notes, time);
        }

        db.Customers.Add(customer);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/v1/customers/{customer.Id.Value}", ToDto(customer));
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        [FromBody] UpdateCustomerRequest request,
        IValidator<UpdateCustomerRequest> validator,
        AzKotleDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var validation = await validator.ValidateAsync(request, ct);
        if (!validation.IsValid)
        {
            return Results.ValidationProblem(validation.ToDictionary());
        }

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == new CustomerId(id), ct);
        if (customer is null)
        {
            return Results.NotFound();
        }

        customer.Rename(request.Name, time);
        customer.SetIco(request.Ico, time);
        customer.SetContactInfo(request.Email, request.Phone, time);
        customer.SetNotes(request.Notes, time);
        await db.SaveChangesAsync(ct);
        return Results.Ok(ToDto(customer));
    }

    private static async Task<IResult> DeleteAsync(
        Guid id,
        AzKotleDbContext db,
        CancellationToken ct)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == new CustomerId(id), ct);
        if (customer is null)
        {
            return Results.NotFound();
        }

        db.Customers.Remove(customer);
        await db.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static CustomerDto ToDto(Customer c) => new(
        c.Id.Value, c.Type, c.Name, c.Ico, c.Email, c.Phone, c.Notes, c.CreatedAt, c.UpdatedAt);
}
