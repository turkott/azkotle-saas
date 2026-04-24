using System.Data.Common;
using AzKotle.Application.Abstractions;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace AzKotle.Infrastructure.Persistence.Interceptors;

public sealed class TenantContextInterceptor : DbConnectionInterceptor
{
    private const string SettingName = "app.current_tenant_id";

    private readonly ITenantContext _tenantContext;

    public TenantContextInterceptor(ITenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await ApplyAsync(connection, cancellationToken);
    }

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Apply(connection);
    }

    private async Task ApplyAsync(DbConnection connection, CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        ConfigureCommand(cmd);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    private void Apply(DbConnection connection)
    {
        using var cmd = connection.CreateCommand();
        ConfigureCommand(cmd);
        cmd.ExecuteNonQuery();
    }

    private void ConfigureCommand(DbCommand cmd)
    {
        var tenantId = _tenantContext.Current;
        cmd.CommandText = "SELECT set_config(@name, @value, false)";

        var nameParam = cmd.CreateParameter();
        nameParam.ParameterName = "name";
        nameParam.Value = SettingName;
        cmd.Parameters.Add(nameParam);

        var valueParam = cmd.CreateParameter();
        valueParam.ParameterName = "value";
        valueParam.Value = tenantId.HasValue ? tenantId.Value.Value.ToString() : string.Empty;
        cmd.Parameters.Add(valueParam);
    }
}
