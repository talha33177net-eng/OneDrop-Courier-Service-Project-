using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Domain.Parcels;
using Domain.Payments;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>The courier's reports: the day's money and counts, each rider's cash, and the returns per merchant.</summary>
public class ReportsTests(WebAppFactory factory) : AppTests(factory)
{
    [Fact]
    public async Task A_delivered_and_a_returned_parcel_show_in_the_day_the_rider_and_the_merchant()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync(area: "Pallabi");
        var rider = await NewRiderAsync("MIR");
        var delivered = await BookAsync(shop.ApiKey, area: "Pallabi", cod: 1000);
        var returned = await BookAsync(shop.ApiKey, area: "Pallabi", cod: 700);
        await using (var scope = await ScopeAsync("onedrop"))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            foreach (var (code, deliver) in new[] { (delivered, true), (returned, false) })
            {
                var parcel = await db.Parcels.SingleAsync(p => p.TrackingCode == code, Cancel);
                parcel.ReceiveAt(parcel.PickupHubId, Today);
                parcel.AssignTo(rider.Id, parcel.CurrentHubId!.Value);
                if (deliver)
                {
                    Assert.True(parcel.Deliver(1000, null, DateTime.UtcNow).IsSuccess);
                }
                else
                {
                    // Refused at the door, back on the shelf, then handed back to the merchant at its pickup hub
                    Assert.True(parcel.Refuse("Customer refused").IsSuccess);
                    parcel.ReceiveAt(parcel.PickupHubId, Today);
                    Assert.True(parcel.ReturnToMerchant(parcel.PickupHubId, DateTime.UtcNow).IsSuccess);
                }

                db.LedgerEntries.AddRange(LedgerEntry.For(parcel, Today));
            }

            await db.SaveChangesAsync(Cancel);
        }

        var admin = await SignInAsync("onedrop", "admin@onedrop.test");
        var today = Today.ToString("yyyy-MM-dd");
        var page = await admin.PageAsync($"/Admin/Reports?From={today}&To={today}");
        Assert.Contains(shop.Name, page);
        Assert.Contains("Returns by merchant", page);
        Assert.Contains("Day by day", page);

        var csv = await admin.GetAsync($"/Admin/Reports?handler=Export&table=days&From={today}&To={today}");
        Assert.Equal(HttpStatusCode.OK, csv.StatusCode);
        var text = await csv.Content.ReadAsStringAsync(Cancel);
        Assert.Contains("day,delivered,returned,cod_collected", text);
        Assert.Contains(Today.ToString("yyyy-MM-dd"), text);

        var riders = await admin.GetAsync($"/Admin/Reports?handler=Export&table=riders&From={today}&To={today}");
        Assert.Contains("rider,hub,runs,delivered", await riders.Content.ReadAsStringAsync(Cancel));
    }

    [Fact]
    public async Task Reports_are_the_courier_s_own_and_another_courier_sees_none_of_them()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync(area: "Banani");
        var code = await BookAsync(shop.ApiKey, area: "Banani", cod: 400);
        await using (var scope = await ScopeAsync("onedrop"))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var parcel = await db.Parcels.SingleAsync(p => p.TrackingCode == code, Cancel);
            parcel.ReceiveAt(parcel.PickupHubId, Today);
            parcel.AssignTo(await db.Riders.Where(r => r.HubId == parcel.PickupHubId).Select(r => r.Id).FirstAsync(Cancel), parcel.CurrentHubId!.Value);
            Assert.True(parcel.Deliver(400, null, DateTime.UtcNow).IsSuccess);
            db.LedgerEntries.AddRange(LedgerEntry.For(parcel, Today));
            await db.SaveChangesAsync(Cancel);
        }

        var today = Today.ToString("yyyy-MM-dd");
        var rival = await SignInAsync("rival", "admin@rival.test");
        Assert.DoesNotContain(shop.Name, await rival.PageAsync($"/Admin/Reports?From={today}&To={today}"));

        // A merchant and hub staff have no reports page at all
        var merchant = await SignInAsync("onedrop", shop.Email);
        Assert.Equal(HttpStatusCode.Redirect, (await merchant.GetAsync("/Admin/Reports")).StatusCode);
    }
}
