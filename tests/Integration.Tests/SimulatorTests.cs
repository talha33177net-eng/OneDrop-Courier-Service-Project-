using Microsoft.EntityFrameworkCore;
using Simulator;

namespace Integration.Tests;

/// <summary>The demo simulator (tools/Simulator) through the real API: its shops and the parcels they book.</summary>
public class SimulatorTests(WebAppFactory factory) : AppTests(factory)
{
    [Fact]
    public async Task The_simulator_books_its_shops_parcels_at_one_courier_priced_from_its_rate_card()
    {
        WebAppFactory.RequireDatabase();
        var tenant = await TenantAsync("rival");
        using var api = Factory.CreateClient();
        var log = new List<string>();

        var result = await Simulation.RunAsync(
            Factory.Services,
            api,
            tenant,
            new SimulationOptions(3, 12, TimeSpan.Zero, Seed: Random.Shared.Next()),
            log.Add,
            Cancel);

        Assert.Equal(12, result.Sent);
        Assert.Empty(result.Refused);
        Assert.Equal(12, log.Count);

        var emails = SimulatedShops.Kinds.Take(3).Select(SimulatedShops.EmailOf).ToList();
        var shopIds = await QueryAsync("rival", db => db.Merchants.Where(m => emails.Contains(m.ContactEmail!)).Select(m => m.Id).ToListAsync(Cancel));
        var parcels = await QueryAsync("rival", db => db.Parcels.Where(p => result.TrackingCodes.Contains(p.TrackingCode)).ToListAsync(Cancel));

        Assert.Equal(3, shopIds.Count);
        Assert.Equal(12, parcels.Count);
        Assert.All(parcels, p => Assert.Equal(tenant.Id, p.TenantId));
        Assert.All(parcels, p => Assert.Contains(p.MerchantId, shopIds));
        Assert.True(parcels.Select(p => p.MerchantId).Distinct().Count() >= 2);
        Assert.All(parcels, p => Assert.True(p.DeliveryCharge >= 70));
        Assert.Equal(parcels.Sum(p => p.TotalCharge), result.Charges);
    }

    [Fact]
    public async Task Each_run_gives_the_shops_a_new_key_and_the_old_one_stops_working()
    {
        WebAppFactory.RequireDatabase();
        var tenant = await TenantAsync("rival");
        var first = await SimulatedShops.EnsureAsync(Factory.Services, tenant, 1, new Random(3), TimeProvider.System, Cancel);
        var second = await SimulatedShops.EnsureAsync(Factory.Services, tenant, 1, new Random(3), TimeProvider.System, Cancel);

        Assert.Equal(first[0].Id, second[0].Id);
        Assert.NotEqual(first[0].ApiKey, second[0].ApiKey);
        using var old = Factory.ClientFor(first[0].ApiKey);
        using var current = Factory.ClientFor(second[0].ApiKey);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, (await old.GetAsync("/api/v1/areas", Cancel)).StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, (await current.GetAsync("/api/v1/areas", Cancel)).StatusCode);
    }
}
