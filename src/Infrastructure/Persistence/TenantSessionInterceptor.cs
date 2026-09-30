using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Application.Abstractions;

namespace Infrastructure.Persistence;

/// <summary>
/// Isolation layer 3, the application's half: every connection the context opens tells SQL Server whose rows it may
/// touch, before its first command. <c>TenantScoped</c> marks it as the application's (so Row-Level Security applies)
/// and <c>TenantId</c> names the tenant, both read only for the life of the connection; with no tenant, the
/// <c>Platform.TenantIsolation</c> policy shows it no tenant's rows. The keys are cleared when the pool resets the
/// connection.
/// </summary>
public class TenantSessionInterceptor(ITenantContext tenantContext) : DbConnectionInterceptor
{
    public const string TenantScopedKey = "TenantScoped";
    public const string TenantIdKey = "TenantId";
    public const string AllTenantsKey = "AllTenants";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = SessionCommand(connection);
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = SessionCommand(connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private DbCommand SessionCommand(DbConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText =
            $"EXEC sys.sp_set_session_context @key = N'{TenantScopedKey}', @value = 1, @read_only = 1;" +
            $" IF @TenantId IS NOT NULL EXEC sys.sp_set_session_context @key = N'{TenantIdKey}', @value = @TenantId, @read_only = 1;";
        var tenantId = command.CreateParameter();
        tenantId.ParameterName = "@TenantId";
        tenantId.DbType = DbType.Int64;
        tenantId.Value = (object?)tenantContext.TenantId ?? DBNull.Value;
        command.Parameters.Add(tenantId);

        return command;
    }
}
