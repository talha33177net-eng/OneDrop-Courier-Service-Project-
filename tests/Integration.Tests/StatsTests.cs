using Microsoft.EntityFrameworkCore;
using Domain.Parcels;

namespace Integration.Tests;

/// <summary>The merchant's stats: its parcels booked in a period it chooses, by the status each is in now.</summary>
public class StatsTests(WebAppFactory factory) : AppTests(factory)
{
    [Fact]
    public async Task Stats_count_the_parcels_booked_in_the_period_with_their_rates_and_cash()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync(area: "Banani");
        var rider = await NewRiderAsync("GUL");
        var waiting = await BookAsync(shop.ApiKey, area: "Gulshan 2", cod: 1000);
        var cancelled = await BookAsync(shop.ApiKey, area: "Gulshan 2", cod: 500);
        var delivered = await BookAsync(shop.ApiKey, area: "Gulshan 2", cod: 1250);
        var partly = await BookAsync(shop.ApiKey, area: "Gulshan 2", cod: 1000);
        var refused = await BookAsync(shop.ApiKey, area: "Gulshan 2", cod: 800);
        var old = await BookAsync(shop.ApiKey, area: "Gulshan 2", cod: 700);
        await QueryAsync("onedrop", async db =>
        {
            var parcels = await db.Parcels.Where(p => p.MerchantId == shop.Id).ToDictionaryAsync(p => p.TrackingCode, Cancel);
            var now = DateTime.UtcNow;
            parcels[cancelled].Cancel("Customer changed their mind", now);
            foreach (var code in new[] { delivered, partly, refused })
            {
                parcels[code].ReceiveAt(parcels[code].DeliveryHubId, Today);
                parcels[code].AssignTo(rider.Id, parcels[code].DeliveryHubId);
            }

            parcels[delivered].Deliver(1250, null, now);
            parcels[partly].Deliver(600, "Kept one of two", now);
            parcels[refused].Refuse("Not at home");
            parcels[refused].ReceiveAt(parcels[refused].DeliveryHubId, Today);

            return await db.SaveChangesAsync(Cancel);
        });

        // Booked forty days ago: outside the last 30 days, inside a custom period that reaches back to it
        var fortyDaysAgo = Today.AddDays(-40);
        await QueryAsync("onedrop", db => db.Parcels
            .Where(p => p.TrackingCode == old)
            .ExecuteUpdateAsync(set => set.SetProperty(p => p.Created, DateTime.UtcNow.AddDays(-40)), Cancel));

        var owner = await SignInAsync("onedrop", shop.Email);
        var stats = await owner.PageAsync("/Merchant/Stats");
        Assert.Contains("Last 30 days", stats);
        Assert.Matches(@"Booked</div><div class=""stat-value"">5</div>", stats);
        Assert.Contains("৳4,550", stats);
        Assert.Contains("2 delivered of 3 that reached an end", stats);
        Assert.Matches(@"Delivery rate</div><div class=""stat-value"">67%</div>", stats);
        Assert.Matches(@"Cancel rate</div><div class=""stat-value"">20%</div>", stats);
        Assert.Matches(@"Return rate</div><div class=""stat-value"">33%</div>", stats);
        Assert.Contains("৳1,850", stats);
        Assert.Contains($"/Merchant/Parcels?tab=PartlyDelivered&from={Today.AddDays(-29):yyyy-MM-dd}&to={Today:yyyy-MM-dd}", stats);
        Assert.DoesNotContain(waiting, stats);

        var custom = await owner.PageAsync($"/Merchant/Stats?period=Custom&from={fortyDaysAgo:yyyy-MM-dd}&to={fortyDaysAgo:yyyy-MM-dd}");
        Assert.Matches(@"Booked</div><div class=""stat-value"">1</div>", custom);
        Assert.Contains("৳700", custom);

        var backwards = await owner.PageAsync($"/Merchant/Stats?period=Custom&from={Today:yyyy-MM-dd}&to={fortyDaysAgo:yyyy-MM-dd}");
        Assert.Contains("Choose a first day that comes before the last.", backwards);
        Assert.Equal(ParcelStatus.Returning, (await ParcelAsync(refused)).Status);
    }

    [Fact]
    public async Task Another_merchant_counts_none_of_these_parcels()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync(area: "Banani");
        var otherShop = await NewMerchantAsync(area: "Banani");
        await BookAsync(shop.ApiKey, area: "Gulshan 2", cod: 900);

        var other = await SignInAsync("onedrop", otherShop.Email);
        var stats = await other.PageAsync("/Merchant/Stats?period=Last7Days");

        Assert.Contains("No parcels were booked in these days", stats);
        Assert.DoesNotContain("৳900", stats);
    }
}
