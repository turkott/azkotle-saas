using System.Net.Http.Headers;
using System.Net.Http.Json;
using AzKotle.Api.IntegrationTests.MultiTenancy;
using AzKotle.Application.Boilers;
using AzKotle.Application.Common;
using AzKotle.Application.Customers;
using AzKotle.Application.Inspections;
using AzKotle.Application.Locations;
using AzKotle.Domain.Entities.Boilers;
using AzKotle.Domain.Entities.Customers;
using AzKotle.Domain.Entities.Inspections;
using AzKotle.Domain.Entities.Users;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace AzKotle.Api.IntegrationTests.Inspections;

public sealed class InspectionPaginationTests : IClassFixture<AzKotleApiFactory>
{
    private readonly AzKotleApiFactory _factory;

    public InspectionPaginationTests(AzKotleApiFactory factory)
    {
        _factory = factory;
    }

    private HttpClient ClientFor()
    {
        var client = _factory.CreateClient();
        var token = _factory.IssueJwt(_factory.TenantAId, _factory.UserAId, "a@example.com", UserRole.Owner);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    [Fact]
    public async Task List_WithIdenticalCreatedAt_PaginatesWithoutDuplicatesOrGaps()
    {
        using var client = ClientFor();
        var boiler = await SeedBoilerAsync(client);

        // Create 5 inspections — natural CreatedAt diverges by microseconds.
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            var resp = await client.PostAsJsonAsync("/api/v1/inspections",
                new CreateInspectionRequest(boiler.Id, InspectionType.AnnualNv191, DateTime.UtcNow.AddHours(-1)));
            resp.EnsureSuccessStatusCode();
            ids.Add((await resp.Content.ReadFromJsonAsync<InspectionDto>())!.Id);
        }

        // Force exact same CreatedAt on all 5 to provoke the tie-breaker scenario.
        // Without the (CreatedAt, Id) cursor, identical timestamps cause skip / dupe
        // behavior under cursor pagination.
        var collisionTs = new DateTime(2026, 04, 26, 12, 00, 00, DateTimeKind.Utc);
        await using (var db = _factory.CreateAdminDbContext())
        {
            var idArray = ids.ToArray();
            await db.Database.ExecuteSqlRawAsync(
                """UPDATE "inspections" SET created_at = {0} WHERE id = ANY({1})""",
                collisionTs, idArray);
        }

        // Page through 2 at a time and accumulate; we expect exactly the 5 unique ids
        // we created (filtering by boiler so we ignore any siblings).
        var collected = new List<Guid>();
        string? cursor = null;
        for (var i = 0; i < 10; i++)
        {
            var url = $"/api/v1/inspections?boilerId={boiler.Id}&pageSize=2"
                + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var resp = await client.GetAsync(url);
            resp.EnsureSuccessStatusCode();
            var page = (await resp.Content.ReadFromJsonAsync<PagedResponse<InspectionDto>>())!;
            collected.AddRange(page.Items.Select(p => p.Id));
            cursor = page.NextCursor;
            if (cursor is null)
            {
                break;
            }
        }

        collected.Should().HaveCount(5, "all five colliding rows must be returned exactly once");
        collected.Distinct().Should().HaveCount(5, "no duplicates across pages");
        collected.Should().BeEquivalentTo(ids, "the same set of ids that were inserted");
    }

    [Fact]
    public async Task List_LegacySingleValueCursor_FailsSoftAndReturnsFirstPage()
    {
        using var client = ClientFor();
        var boiler = await SeedBoilerAsync(client);
        for (var i = 0; i < 3; i++)
        {
            (await client.PostAsJsonAsync("/api/v1/inspections",
                new CreateInspectionRequest(boiler.Id, InspectionType.AnnualNv191, DateTime.UtcNow.AddHours(-2 - i))))
                .EnsureSuccessStatusCode();
        }

        // Pre-F10 cursor format encoded only Ticks (8 bytes). Ensure the new
        // endpoint returns the first page rather than 500-ing on the stale shape.
        var legacyCursor = Convert.ToBase64String(BitConverter.GetBytes(DateTime.UtcNow.Ticks))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var resp = await client.GetAsync(
            $"/api/v1/inspections?boilerId={boiler.Id}&cursor={Uri.EscapeDataString(legacyCursor)}");

        resp.EnsureSuccessStatusCode();
        var page = (await resp.Content.ReadFromJsonAsync<PagedResponse<InspectionDto>>())!;
        page.Items.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    private static async Task<BoilerDto> SeedBoilerAsync(HttpClient client)
    {
        var customerResp = await client.PostAsJsonAsync("/api/v1/customers",
            new CreateCustomerRequest(CustomerType.Company, "PageTest " + Guid.NewGuid().ToString("N")[..6]));
        customerResp.EnsureSuccessStatusCode();
        var customer = (await customerResp.Content.ReadFromJsonAsync<CustomerDto>())!;

        var locationResp = await client.PostAsJsonAsync("/api/v1/locations",
            new CreateLocationRequest(customer.Id, "Ulice 1", "Praha", "11000"));
        locationResp.EnsureSuccessStatusCode();
        var location = (await locationResp.Content.ReadFromJsonAsync<LocationDto>())!;

        var boilerResp = await client.PostAsJsonAsync("/api/v1/boilers",
            new CreateBoilerRequest(location.Id, "Vaillant", "ecoTEC",
                "SN-" + Guid.NewGuid().ToString("N")[..8],
                24m, FuelType.NaturalGas, new DateOnly(2024, 01, 01)));
        boilerResp.EnsureSuccessStatusCode();
        return (await boilerResp.Content.ReadFromJsonAsync<BoilerDto>())!;
    }
}
