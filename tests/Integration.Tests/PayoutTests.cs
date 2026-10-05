using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Application.Payments.AdminPayouts;
using Application.Payments.RunPayouts;
using Domain.Parcels;
using Domain.Payments;
using Infrastructure.Payments;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>Next-day payouts: each merchant paid its cash less its charges, once, with charges carried forward.</summary>
public class PayoutTests(WebAppFactory factory) : AppTests(factory)
{
    [Fact]
    public async Task Yesterdays_lines_are_paid_once_and_a_merchant_whose_charges_are_more_waits()
    {
        WebAppFactory.RequireDatabase();
        var paid = await NewMerchantAsync();
        var owing = await NewMerchantAsync();
        var delivered = await DeliveredAsync(paid, cod: 1000);
        var returned = await ReturnedAsync(owing);
        var tomorrow = DateTime.UtcNow.AddDays(1);

        // While today lasts nothing is paid; the next day the job pays the merchant its ৳1,000 less ৳70
        var today = await RunAsync(DateTime.UtcNow);
        Assert.DoesNotContain(today.Payouts, p => p.MerchantId == paid.Id);
        await RunAsync(tomorrow);
        await RunAsync(tomorrow.AddMinutes(30));

        var payouts = await QueryAsync("onedrop", db => db.Payouts.Where(p => p.MerchantId == paid.Id).ToListAsync(Cancel));
        var payout = Assert.Single(payouts);
        Assert.Equal((1000m, 70m, 930m, PayoutStatus.Paid), (payout.CodTotal, payout.ChargesTotal, payout.Amount, payout.Status));
        Assert.Matches("^INV-[0-9]{6}$", payout.Number);
        var sent = Factory.Services.GetRequiredService<FakePayoutLog>().Recent.Where(p => p.Key == $"onedrop-payout-{payout.Id}").ToList();
        Assert.Equal(930, Assert.Single(sent).Amount);

        // The merchant who owes the return charges is paid nothing; its lines wait
        Assert.Empty(await QueryAsync("onedrop", db => db.Payouts.Where(p => p.MerchantId == owing.Id).ToListAsync(Cancel)));
        Assert.All(
            await QueryAsync("onedrop", db => db.LedgerEntries.Where(e => e.ParcelId == returned).ToListAsync(Cancel)),
            line => Assert.Null(line.PayoutId));

        // The merchant sees its invoice and its parcel on it; another merchant gets a 404
        var merchant = await SignInAsync("onedrop", paid.Email);
        var invoice = await merchant.PageAsync($"/Merchant/Payment/{payout.Number}");
        Assert.Contains(delivered, invoice);
        Assert.Contains("৳930", invoice);
        var other = await SignInAsync("onedrop", owing.Email);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/Merchant/Payment/{payout.Number}")).StatusCode);
        Assert.Contains(payout.Number, await (await SignInAsync("onedrop", "admin@onedrop.test")).PageAsync("/Admin/Payouts"));
    }

    [Fact]
    public async Task Lines_from_today_are_owed_but_payable_only_from_tomorrow()
    {
        WebAppFactory.RequireDatabase();
        var shop = await NewMerchantAsync();
        await DeliveredAsync(shop, cod: 1000);

        var today = await OwedAsync(shop.Id, DateTime.UtcNow);
        Assert.Equal(0, today.Payable);
        Assert.True(today.DueTomorrow > 0);

        var tomorrow = await OwedAsync(shop.Id, DateTime.UtcNow.AddDays(1));
        Assert.Equal(today.Net, tomorrow.Payable);
        Assert.Equal(0, tomorrow.DueTomorrow);
    }

    private async Task<MerchantBalance> OwedAsync(long merchantId, DateTime utcNow)
    {
        await using var scope = await ScopeAsync("onedrop");
        var handler = ActivatorUtilities.CreateInstance<AdminPayoutsHandler>(scope.ServiceProvider, (TimeProvider)new FakeTimeProvider(utcNow));
        var overview = await handler.GetAsync(null, Cancel);

        return overview.Owed.Single(m => m.MerchantId == merchantId);
    }

    private async Task<(PayoutRun Run, IReadOnlyList<Payout> Payouts)> RunAsync(DateTime utcNow)
    {
        await using var scope = await ScopeAsync("onedrop");
        var job = ActivatorUtilities.CreateInstance<PayoutsJob>(scope.ServiceProvider, (TimeProvider)new FakeTimeProvider(utcNow));
        var run = await job.PayAsync(Cancel);

        return (run, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Payouts.ToListAsync(Cancel));
    }

    private async Task<string> DeliveredAsync(TestMerchant shop, decimal cod)
    {
        var code = await BookAsync(shop.ApiKey, area: "Pallabi", cod: cod);
        await FinishAsync(code, (parcel, rider) =>
        {
            parcel.AssignTo(rider, parcel.CurrentHubId!.Value);

            return parcel.Deliver(cod, null, DateTime.UtcNow);
        });

        return code;
    }

    private async Task<long> ReturnedAsync(TestMerchant shop)
    {
        var code = await BookAsync(shop.ApiKey, area: "Pallabi", cod: 500);

        return await FinishAsync(code, (parcel, _) =>
        {
            parcel.RequestReturn("Merchant asked");

            return parcel.ReturnToMerchant(parcel.CurrentHubId!.Value, DateTime.UtcNow);
        });
    }

    /// <summary>Scans a parcel in at its hub and takes it through <paramref name="finish"/>, writing its ledger lines dated today.</summary>
    private async Task<long> FinishAsync(string code, Func<Parcel, long, Domain.Common.Result> finish)
    {
        await using var scope = await ScopeAsync("onedrop");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var parcel = await db.Parcels.SingleAsync(p => p.TrackingCode == code, Cancel);
        var rider = await db.Riders.Where(r => r.HubId == parcel.PickupHubId).Select(r => r.Id).FirstAsync(Cancel);
        parcel.ReceiveAt(parcel.PickupHubId);
        Assert.True(finish(parcel, rider).IsSuccess);

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka")));
        db.LedgerEntries.AddRange(LedgerEntry.For(parcel, today));
        await db.SaveChangesAsync(Cancel);

        return parcel.Id;
    }
}
