using AzKotle.Application.Abstractions;
using AzKotle.Domain.Common;

namespace AzKotle.Infrastructure.MultiTenancy;

public sealed class AmbientTenantContext : ITenantContext
{
    private static readonly AsyncLocal<TenantId?> _current = new();

    public TenantId? Current => _current.Value;

    public IDisposable BeginScope(TenantId tenantId)
    {
        var previous = _current.Value;
        _current.Value = tenantId;
        return new Scope(() => _current.Value = previous);
    }

    private sealed class Scope : IDisposable
    {
        private readonly Action _onDispose;
        private bool _disposed;

        public Scope(Action onDispose) => _onDispose = onDispose;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _onDispose();
        }
    }
}
