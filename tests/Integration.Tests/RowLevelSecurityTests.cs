using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Domain.Delivery;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>
/// Isolation layer 3: SQL Server's Row-Level Security (<c>Platform.TenantIsolation</c>) keeps a tenant's connection to
/// its own rows even where the query filter is lifted and the save interceptor is bypassed.
/// </summary>
public class RowLevelSecurityTests(WebAppFactory factory)
{
    [Fact]
    public async Task Every_table_with_a_tenant_is_filtered_and_blocked()
    {
        WebAppFactory.RequireDatabase();
        const string sql = """
            SELECT
                S.name + '.' + T.name,
                COALESCE(P.predicate_type_desc + ' ' + P.operation_desc, P.predicate_type_desc, '')
            FROM
                sys.tables T
                JOIN
                sys.schemas S
                    ON S.schema_id = T.schema_id
                JOIN
                sys.columns C
                    ON C.object_id = T.object_id AND C.name = 'TenantId'
                LEFT JOIN
                (
                    sys.security_predicates P
                    JOIN
                    sys.security_policies Y
                        ON Y.object_id = P.object_id AND Y.name = 'TenantIsolation' AND Y.is_enabled = 1
                )
                    ON P.target_object_id = T.object_id AND P.predicate_definition LIKE '%TenantAccess%(%TenantId%)%'
            WHERE
                S.name NOT IN ('Platform', 'HangFire')
            """;

        var predicates = new Dictionary<string, HashSet<string>>();
        await using var connection = new SqlConnection(WebAppFactory.ConnectionString);
        await connection.OpenAsync(Cancel);
        await using var command = new SqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(Cancel);
        while (await reader.ReadAsync(Cancel))
        {
            var table = reader.GetString(0);
            (predicates.TryGetValue(table, out var found) ? found : predicates[table] = []).Add(reader.GetString(1));
        }

        Assert.Contains("Parcels.Parcel", predicates.Keys);
        Assert.Contains("Identity.User", predicates.Keys);
        Assert.All(predicates, table => Assert.True(
            table.Value.IsSupersetOf(["FILTER", "BLOCK AFTER INSERT", "BLOCK AFTER UPDATE"]),
            $"Add {table.Key} to Platform.TenantIsolation (filter, block after insert and after update)."));
    }

    [Fact]
    public async Task With_the_query_filter_lifted_the_database_still_hides_another_tenants_rows()
    {
        WebAppFactory.RequireDatabase();
        var (onedrop, rival) = (await TenantAsync("onedrop"), await TenantAsync("rival"));
        await using var scope = ScopeFor(rival);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantsSeen = () => db.Hubs.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Select(h => h.TenantId).Distinct().ToListAsync(Cancel);

        Assert.Equal([rival.Id], await tenantsSeen());
        await using (await db.AcrossTenantsAsync(Cancel))
        {
            Assert.Contains(onedrop.Id, await tenantsSeen());
        }

        Assert.Equal([rival.Id], await tenantsSeen());
        Assert.Empty(await db.Parcels.IgnoreQueryFilters([AppDbContext.TenantFilter, AppDbContext.MerchantFilter])
            .Where(o => o.TenantId == onedrop.Id).Select(o => o.Id).Take(1).ToListAsync(Cancel));
    }

    [Fact]
    public async Task Without_a_tenant_the_database_shows_only_platform_logins()
    {
        WebAppFactory.RequireDatabase();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(0, await db.Hubs.IgnoreQueryFilters([AppDbContext.TenantFilter]).CountAsync(Cancel));
        var logins = await db.Users.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Select(u => u.TenantId).Distinct().ToListAsync(Cancel);
        Assert.Equal([null], logins);
    }

    [Fact]
    public async Task Writes_that_bypass_the_save_interceptor_cannot_reach_another_tenant()
    {
        WebAppFactory.RequireDatabase();
        var (onedrop, rival) = (await TenantAsync("onedrop"), await TenantAsync("rival"));
        await using var scope = ScopeFor(rival);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hubId = await db.Hubs.Select(h => h.Id).FirstAsync(Cancel);
        var rider = Rider.Create(hubId, "RLS test", "019" + Random.Shared.Next(0, 100_000_000).ToString("D8"), null).Value;
        db.Riders.Add(rider);
        await db.SaveChangesAsync(Cancel);

        // The updates write each row's own value back, so a broken policy counts the rows without changing them
        var changed = await db.Hubs.IgnoreQueryFilters([AppDbContext.TenantFilter])
            .Where(h => h.TenantId == onedrop.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(h => h.Name, h => h.Name), Cancel);
        var touched = await db.Database.ExecuteSqlAsync(
            $"UPDATE Network.Zone SET [Name] = [Name] WHERE TenantId = {onedrop.Id}", Cancel);
        var inserted = await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlAsync(
            $"INSERT INTO Delivery.Rider (TenantId, HubId, Phone, Name) VALUES ({onedrop.Id}, {hubId}, {rider.Phone}, N'Planted')", Cancel));
        var moved = await Assert.ThrowsAsync<SqlException>(() => db.Riders
            .Where(c => c.Id == rider.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(c => c.TenantId, onedrop.Id), Cancel));

        Assert.Equal(0, changed);
        Assert.Equal(0, touched);
        Assert.Contains("block predicate", inserted.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("block predicate", moved.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(rival.Id, await db.Riders.Where(c => c.Id == rider.Id).Select(c => c.TenantId).SingleAsync(Cancel));
    }

    [Fact]
    public async Task A_pooled_connection_never_keeps_the_last_tenant()
    {
        WebAppFactory.RequireDatabase();
        TenantInfo[] tenants = [await TenantAsync("onedrop"), await TenantAsync("rival")];

        for (var round = 0; round < 6; round++)
        {
            var tenant = tenants[round % 2];
            await using var scope = ScopeFor(tenant);
            var seen = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Zones
                .IgnoreQueryFilters([AppDbContext.TenantFilter])
                .Select(z => z.TenantId).Distinct().ToListAsync(Cancel);

            Assert.Equal([tenant.Id], seen);
        }
    }

    [Fact]
    public async Task A_connection_that_is_not_the_applications_is_not_restricted()
    {
        WebAppFactory.RequireDatabase();
        await using var connection = new SqlConnection(WebAppFactory.ConnectionString);
        await connection.OpenAsync(Cancel);
        await using var command = new SqlCommand("SELECT COUNT(DISTINCT TenantId) FROM Network.Hub", connection);

        // DbUp, SqlPackage and an operator's own queries see every tenant, as they must to migrate the data
        Assert.True((int)(await command.ExecuteScalarAsync(Cancel))! >= 2);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    private async Task<TenantInfo> TenantAsync(string slug)
    {
        return (await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync(slug, Cancel))!;
    }

    private AsyncServiceScope ScopeFor(TenantInfo tenant)
    {
        var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant);

        return scope;
    }
}
