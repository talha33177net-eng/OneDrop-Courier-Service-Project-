using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Domain.Delivery;
using Domain.Network;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>Isolation layer 2: even code that bypasses the query filters cannot write to another tenant's rows.</summary>
public class SaveInterceptorTests(WebAppFactory factory)
{
    [Fact]
    public async Task Changing_another_tenants_row_is_refused()
    {
        WebAppFactory.RequireDatabase();
        await using var scope = await ScopeForAsync("rival");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var onedrop = await TenantIdAsync("onedrop");
        Hub onedropHub;
        await using (await db.AcrossTenantsAsync(TestContext.Current.CancellationToken))
        {
            onedropHub = await db.Hubs
                .IgnoreQueryFilters([AppDbContext.TenantFilter])
                .FirstAsync(h => h.TenantId == onedrop, TestContext.Current.CancellationToken);
        }

        db.Entry(onedropHub).Property(h => h.Name).CurrentValue = "Hijacked";

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
        Assert.Contains("Refused to write Hub", refused.Message);
    }

    [Fact]
    public async Task A_new_row_takes_the_current_tenant()
    {
        WebAppFactory.RequireDatabase();
        await using var scope = await ScopeForAsync("rival");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hubId = await db.Hubs.Select(h => h.Id).FirstAsync(TestContext.Current.CancellationToken);

        var rider = Rider.Create(hubId, "Interceptor test", "01900000000", Vehicle.Motorbike, null).Value;
        db.Riders.Add(rider);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        Assert.Equal(await TenantIdAsync("rival"), rider.TenantId);
        Assert.NotEqual(default, rider.Created);
    }

    [Fact]
    public async Task Saving_without_a_tenant_is_refused()
    {
        WebAppFactory.RequireDatabase();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Riders.Add(Rider.Create(1, "No tenant", "01900000000", Vehicle.Motorbike, null).Value);

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Without_a_tenant_the_query_filter_returns_nothing()
    {
        WebAppFactory.RequireDatabase();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(0, await db.Hubs.CountAsync(TestContext.Current.CancellationToken));
        await using (await db.AcrossTenantsAsync(TestContext.Current.CancellationToken))
        {
            Assert.True(await db.Hubs.IgnoreQueryFilters([AppDbContext.TenantFilter]).AnyAsync(TestContext.Current.CancellationToken));
        }
    }

    private async Task<AsyncServiceScope> ScopeForAsync(string slug)
    {
        var tenant = await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync(slug, TestContext.Current.CancellationToken);
        var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant!);

        return scope;
    }

    private async Task<long> TenantIdAsync(string slug)
    {
        var tenant = await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync(slug, TestContext.Current.CancellationToken);

        return tenant!.Id;
    }
}
