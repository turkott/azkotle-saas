using AzKotle.Application.Abstractions;
using AzKotle.Domain.Entities.Boilers;
using AzKotle.Domain.Entities.Customers;
using AzKotle.Domain.Entities.Inspections;
using AzKotle.Domain.Entities.Locations;
using AzKotle.Domain.Entities.Users;
using AzKotle.Infrastructure.Persistence;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace AzKotle.Api.Endpoints;

public static class DashboardEndpoints
{
    // F21: dashboard window — overdue do 7 dní + nadcházejících 30 dní. Owner se chce
    // proaktivně chytat odcházejících klientů, ale revize starší než týden už typicky
    // mají náhradního dodavatele → filtrovat venku.
    private const int OverdueWindowDays = 7;
    private const int UpcomingWindowDays = 30;

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/dashboard").RequireAuthorization();
        group.MapGet("/summary", GetSummaryAsync).WithName("DashboardSummary");
        group.MapGet("/expirations", GetExpirationsAsync).WithName("DashboardExpirations");
        return routes;
    }

    private static async Task<IResult> GetSummaryAsync(
        AzKotleDbContext db,
        ITenantContext tenantContext,
        CancellationToken ct)
    {
        if (tenantContext.Current is not { } tenantId)
        {
            return Results.Unauthorized();
        }

        // Explicit TenantId filter is defense-in-depth on top of Postgres RLS — same
        // pattern as TeamService.ListAsync. Counts run as single-roundtrip aggregates.
        var totalInspections = await db.Inspections.AsNoTracking()
            .Where(i => i.TenantId == tenantId)
            .CountAsync(ct);

        var totalSignedInspections = await db.Inspections.AsNoTracking()
            .Where(i => i.TenantId == tenantId && i.Status == InspectionStatus.Signed)
            .CountAsync(ct);

        var totalActiveTechnicians = await db.Users.AsNoTracking()
            .Where(u => u.TenantId == tenantId && u.Role == UserRole.Technician && u.IsActive)
            .CountAsync(ct);

        var tenant = await db.Tenants.AsNoTracking()
            .Where(t => t.Id == tenantId)
            .Select(t => new { t.CompanyName })
            .FirstOrDefaultAsync(ct);

        return Results.Ok(new DashboardSummaryResponse(
            CompanyName: tenant?.CompanyName ?? string.Empty,
            TotalInspections: totalInspections,
            TotalSignedInspections: totalSignedInspections,
            TotalActiveTechnicians: totalActiveTechnicians));
    }

    private static async Task<IResult> GetExpirationsAsync(
        AzKotleDbContext db,
        ITenantContext tenantContext,
        TimeProvider time,
        CancellationToken ct)
    {
        if (tenantContext.Current is not { } tenantId)
        {
            return Results.Unauthorized();
        }

        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var rangeStart = today.AddDays(-OverdueWindowDays);
        var rangeEnd = today.AddDays(UpcomingWindowDays);

        // Latest signed AnnualNv191 inspekce per boiler (DistinctBy v EF Core 7+
        // přeloží na ROW_NUMBER() OVER (PARTITION BY boiler_id ORDER BY ...) =1).
        // Stejný idiom jako v PostgreSQL window-function "greatest-n-per-group".
        var latestPerBoiler = db.Inspections.AsNoTracking()
            .Where(i => i.TenantId == tenantId
                        && i.Status == InspectionStatus.Signed
                        && i.Type == InspectionType.AnnualNv191)
            .OrderByDescending(i => i.PerformedAt)
            .ThenByDescending(i => i.Id)
            .GroupBy(i => i.BoilerId)
            .Select(g => g.First());

        var rows = await (from i in latestPerBoiler
                          where i.NextDueAt != null
                                && i.NextDueAt >= rangeStart
                                && i.NextDueAt <= rangeEnd
                          join b in db.Boilers.AsNoTracking() on i.BoilerId equals b.Id
                          join l in db.Locations.AsNoTracking() on b.LocationId equals l.Id
                          join c in db.Customers.AsNoTracking() on l.CustomerId equals c.Id
                          orderby i.NextDueAt
                          select new
                          {
                              InspectionId = i.Id,
                              BoilerId = b.Id,
                              CustomerId = c.Id,
                              CustomerName = c.Name,
                              BoilerManufacturer = b.Manufacturer,
                              BoilerModel = b.Model,
                              BoilerQrCode = b.QrCode,
                              NextDueAt = i.NextDueAt!.Value,
                          })
                          .ToListAsync(ct);

        var dtos = rows.Select(r => new UpcomingExpirationResponse(
            InspectionId: r.InspectionId.Value,
            BoilerId: r.BoilerId.Value,
            BoilerName: $"{r.BoilerManufacturer} {r.BoilerModel} ({r.BoilerQrCode})",
            CustomerId: r.CustomerId.Value,
            CustomerName: r.CustomerName,
            NextDueAt: r.NextDueAt,
            DaysRemaining: r.NextDueAt.DayNumber - today.DayNumber)).ToList();

        return Results.Ok(dtos);
    }
}

public sealed record DashboardSummaryResponse(
    string CompanyName,
    int TotalInspections,
    int TotalSignedInspections,
    int TotalActiveTechnicians);

public sealed record UpcomingExpirationResponse(
    Guid InspectionId,
    Guid BoilerId,
    string BoilerName,
    Guid CustomerId,
    string CustomerName,
    DateOnly NextDueAt,
    int DaysRemaining);
