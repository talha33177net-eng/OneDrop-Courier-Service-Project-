using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;
using Simulator;

namespace Integration.Tests;

/// <summary>The demo simulator (tools/Simulator) through the real API: its shops, its orders and their deliveries.</summary>
public class SimulatorTests(WebAppFactory factory)
{
    [Fact]
    public async Task The_simulator_sends_its_shops_orders_that_group_into_deliveries_of_one_operator()
    {
        WebAppFactory.RequireDatabase();
        var tenant = (await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync("chattogram", Cancel))!;
        using var api = factory.CreateClient();
        var log = new List<string>();

        var result = await Simulation.RunAsync(
            factory.Services, api, tenant, new SimulationOptions(3, 12, TimeSpan.Zero, Seed: 11), log.Add, Cancel);

        Assert.Equal(12, result.Sent);
        Assert.Empty(result.Refused);
        Assert.InRange(result.Joined, 1, 11);
        Assert.Equal(12, log.Count);

        await using var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var emails = SimulatedShops.Kinds.Take(3).Select(SimulatedShops.EmailOf).ToList();
        var shopIds = await db.Merchants.Where(m => emails.Contains(m.ContactEmail!)).Select(m => m.Id).ToListAsync(Cancel);
        var orders = await db.Orders
            .Where(o => result.Numbers.Contains(o.Number))
            .Select(o => new { o.TenantId, o.MerchantId, o.DeliveryGroupId, o.AddedFee })
            .ToListAsync(Cancel);

        Assert.Equal(3, shopIds.Count);
        Assert.Equal(12, orders.Count);
        Assert.All(orders, o => Assert.Equal(tenant.Id, o.TenantId));
        Assert.All(orders, o => Assert.Contains(o.MerchantId, shopIds));
        Assert.Contains(orders.GroupBy(o => o.DeliveryGroupId), delivery => delivery.Select(o => o.MerchantId).Distinct().Count() >= 2);
        Assert.Contains(orders, o => o.AddedFee == tenant.ExtraShopFee);
    }

    [Fact]
    public async Task Each_run_gives_the_shops_a_new_key_and_the_old_one_stops_working()
    {
        WebAppFactory.RequireDatabase();
        var tenant = (await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync("chattogram", Cancel))!;
        var first = await SimulatedShops.EnsureAsync(factory.Services, tenant, 1, new Random(3), TimeProvider.System, Cancel);
        var second = await SimulatedShops.EnsureAsync(factory.Services, tenant, 1, new Random(3), TimeProvider.System, Cancel);

        Assert.Equal(first[0].Id, second[0].Id);
        Assert.NotEqual(first[0].ApiKey, second[0].ApiKey);
        using var old = factory.ClientFor(first[0].ApiKey);
        using var current = factory.ClientFor(second[0].ApiKey);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await old.GetAsync("/api/v1/areas", Cancel)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await current.GetAsync("/api/v1/areas", Cancel)).StatusCode);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;
}
