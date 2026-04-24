using AzKotle.Domain.Common;

namespace AzKotle.Application.Abstractions;

public interface ITenantContext
{
    TenantId? Current { get; }
}
