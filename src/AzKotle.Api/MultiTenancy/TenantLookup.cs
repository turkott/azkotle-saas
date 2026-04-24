using AzKotle.Domain.Common;
using AzKotle.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace AzKotle.Api.MultiTenancy;

internal sealed class TenantLookup
{
    private static readonly TimeSpan _cacheTtl = TimeSpan.FromMinutes(5);

    private readonly AzKotleDbContext _db;
    private readonly IMemoryCache _cache;

    public TenantLookup(AzKotleDbContext db, IMemoryCache cache)
    {
        _db = db;
        _cache = cache;
    }

    public async Task<TenantId?> ResolveBySlugAsync(string slug, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(slug))
        {
            return null;
        }

        var cacheKey = $"tenant-slug:{slug}";
        if (_cache.TryGetValue(cacheKey, out TenantId? cached))
        {
            return cached;
        }

        var tenantGuid = await _db.Tenants
            .AsNoTracking()
            .Where(t => t.Slug == slug)
            .Select(t => (Guid?)t.Id.Value)
            .FirstOrDefaultAsync(cancellationToken);

        TenantId? resolved = tenantGuid.HasValue ? new TenantId(tenantGuid.Value) : null;
        _cache.Set(cacheKey, resolved, _cacheTtl);
        return resolved;
    }
}
