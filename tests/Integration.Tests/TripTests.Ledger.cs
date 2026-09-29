using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Application.Common;
using Application.Delivery.Door;
using Application.Delivery.HubCash;
using Application.Payments.SettleMerchants;
using Domain.Delivery;
using Domain.Merchants;
using Domain.Orders;
using Domain.Payments;
using Infrastructure.Identity;
using Infrastructure.Payments;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>
/// Task 3.7: the shops' ledger written at the door and at Start trip, the next-day payout with charges carried
/// forward, and the riders' cash handed in at the hub. Payouts are made with a fake clock a day or two ahead, and only
/// this test's own shops are checked: the settle job pays every shop of the operator.
/// </summary>
public partial class TripTests
{
    [Fact]
    public async Task The_door_owes_each_shop_its_cod_from_the_payment_and_charges_the_shop_whose_order_comes_back()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var hub = await NewHubAsync();
        var rider = await NewRiderAsync(hub, "Ledger rider", new TripLoad(30, 25_000));
        var phone = NewPhone();
        var fashion = await CreateAsync(WebAppFactory.DhakaFashion, phone, hub, cod: 1200);
        var gadget = await CreateAsync(WebAppFactory.DhakaGadget, phone, hub, cod: 800);
        var beauty = await CreateAsync(WebAppFactory.DhakaBeauty, phone, hub, cod: 500);
        var delivery = await DueAsync(hub, [fashion, gadget, beauty]);
        await PlanAsync("dhaka", hub);
        await StartAsync("dhaka", rider.UserId!.Value);
        var userId = rider.UserId.Value;

        var due = (await DoorAsync("dhaka", door => door.DueAsync(userId, delivery, [beauty], Cancel))).Value;
        await DoorAsync("dhaka", door => door.HandOverAsync(userId, delivery, [beauty], due.Total, PaymentMethod.Cash, Cancel));

        var payment = Assert.Single(await PaymentsOfAsync(delivery));
        var day = await TodayDateAsync();
        Assert.Equal([new LedgerRow(LedgerEntryKind.Cod, 1200, payment.Id, day, null)], await LedgerOfAsync(fashion));
        Assert.Equal([new LedgerRow(LedgerEntryKind.Cod, 800, payment.Id, day, null)], await LedgerOfAsync(gadget));
        Assert.Equal([new LedgerRow(LedgerEntryKind.ReturnCharge, -dhaka.ReturnCharge, null, day, null)], await LedgerOfAsync(beauty));

        // The ledger splits the payment's COD over its shops exactly; the fee is the operator's
        Assert.Equal(payment.Cod, await CodOfPaymentAsync(payment.Id));
    }

    [Fact]
    public async Task A_shop_that_had_not_handed_its_order_over_pays_the_late_fee_and_one_whose_parcel_was_with_us_does_not()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var hub = await NewHubAsync();
        var rider = await NewRiderAsync(hub, "Late rider", new TripLoad(30, 25_000));
        var phone = NewPhone();
        var ready = await CreateAsync(WebAppFactory.DhakaFashion, phone, hub);
        var atTheShop = await CreateAsync(WebAppFactory.DhakaGadget, phone, hub);
        var halfIn = await CreateAsync(WebAppFactory.DhakaBeauty, phone, hub, packages: 2);
        await DueAsync(hub, [ready, atTheShop, halfIn], receive: false);
        await ReceiveAsync(hub, $"{ready}-1");
        await ReceiveAsync(hub, $"{halfIn}-1");
        await PlanAsync("dhaka", hub);

        var started = await StartAsync("dhaka", rider.UserId!.Value);

        Assert.Equal(2, started.Value.OrdersFollowing);
        Assert.Equal(
            [new LedgerRow(LedgerEntryKind.LateHandoverFee, -dhaka.LateHandoverFee, null, await TodayDateAsync(), null)],
            await LedgerOfAsync(atTheShop));
        Assert.Empty(await LedgerOfAsync(halfIn));
        Assert.Empty(await LedgerOfAsync(ready));
    }

    [Fact]
    public async Task Each_shop_is_paid_yesterdays_cod_less_its_charges_and_a_debt_comes_off_the_next_payout()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var hub = await NewHubAsync();
        var rider = await NewRiderAsync(hub, "Payout rider", new TripLoad(30, 25_000));
        var paid = await NewShopAsync(hub, withLogin: true);
        var owing = await NewShopAsync(hub);
        var (x, y, z) = (NewPhone(), NewPhone(), NewPhone());
        var delivered = await CreateAsync(paid.ApiKey, x, hub, cod: 1000);
        var refused = await CreateAsync(paid.ApiKey, y, hub, cod: 700);
        var alsoRefused = await CreateAsync(owing.ApiKey, y, hub, cod: 400);
        var nextDay = await CreateAsync(owing.ApiKey, z, hub, cod: 500);
        var toX = await DueAsync(hub, [delivered]);
        var toY = await DueAsync(hub, [refused, alsoRefused]);
        var toZ = await DueAsync(hub, [nextDay]);
        await PlanAsync("dhaka", hub);
        await StartAsync("dhaka", rider.UserId!.Value);
        var userId = rider.UserId.Value;
        foreach (var (stop, refusing) in new[] { (toX, Array.Empty<string>()), (toY, [refused, alsoRefused]), (toZ, []) })
        {
            var due = (await DoorAsync("dhaka", door => door.DueAsync(userId, stop, refusing, Cancel))).Value;
            Assert.True((await DoorAsync("dhaka", door => door.HandOverAsync(userId, stop, refusing, due.Total, PaymentMethod.Cash, Cancel))).IsSuccess);
        }

        // The last COD belongs to tomorrow, as if delivered then
        var today = await TodayDateAsync();
        await using (var scope = await ScopeAsync("dhaka"))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE [Payments].[LedgerEntry] SET [EntryDate] = {today.AddDays(1)} WHERE [OrderId] = (SELECT [Id] FROM [Orders].[Order] WHERE [Number] = {nextDay})",
                Cancel);
        }

        // Today's lines wait while today lasts
        await SettleAsync(today);
        Assert.Empty(await SettlementsOfAsync(paid.Id));

        // Next morning: the paying shop gets its COD less the return; the other owes the return and gets nothing
        await SettleAsync(today.AddDays(1));
        await SettleAsync(today.AddDays(1));
        var first = Assert.Single(await SettlementsOfAsync(paid.Id));
        Assert.Equal((today, 1000 - dhaka.ReturnCharge, SettlementStatus.Paid, "01711999999"), (first.UpToDate, first.Amount, first.Status, first.Account));
        Assert.All(await LedgerOfAsync(delivered, refused), line => Assert.Equal(first.Id, line.SettlementId));
        var sent = Assert.Single(factory.Services.GetRequiredService<FakePayoutLog>().Recent, p => p.Reference == first.GatewayReference);
        Assert.Equal((first.Amount, "01711999999", $"dhaka-settlement-{first.Id}"), (sent.Amount, sent.Account, sent.Key));
        Assert.Empty(await SettlementsOfAsync(owing.Id));
        Assert.Null(Assert.Single(await LedgerOfAsync(alsoRefused)).SettlementId);

        // The day after: the carried return comes off the COD delivered since
        await SettleAsync(today.AddDays(2));
        var carried = Assert.Single(await SettlementsOfAsync(owing.Id));
        Assert.Equal((today.AddDays(1), 500 - dhaka.ReturnCharge), (carried.UpToDate, carried.Amount));
        Assert.Equal([carried.Id, carried.Id], (await LedgerOfAsync(alsoRefused, nextDay)).Select(line => line.SettlementId));
        Assert.Single(await SettlementsOfAsync(paid.Id));

        // Every payout is exactly its lines
        foreach (var settlement in (await SettlementsOfAsync(paid.Id)).Concat(await SettlementsOfAsync(owing.Id)))
        {
            Assert.Equal(settlement.Amount, await AmountOfSettlementAsync(settlement.Id));
        }

        // The shop sees its own payout and lines; another shop sees none of them
        var shopClient = await SignInAsync("dhaka", paid.Email!);
        var page = await shopClient.GetStringAsync("/Merchant/Payouts", Cancel);
        var gadgetPage = await (await SignInAsync("dhaka", "gadget@dhaka.onedrop.test")).GetStringAsync("/Merchant/Payouts", Cancel);
        Assert.Contains($"৳{first.Amount:N0}", page);
        Assert.Contains(delivered, page);
        Assert.Contains("Return charge", page);
        Assert.Contains($"−৳{dhaka.ReturnCharge:N0}", page);
        Assert.DoesNotContain(delivered, gadgetPage);
        Assert.DoesNotContain(nextDay, page);
    }

    [Fact]
    public async Task The_hub_records_the_riders_cash_once_every_stop_is_done_and_the_rider_sees_it()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var hub = await NewHubAsync();
        var rider = await NewRiderAsync(hub, "Cash in rider", new TripLoad(30, 25_000));
        var first = await CreateAsync(WebAppFactory.DhakaFashion, NewPhone(), hub, cod: 650);
        var second = await CreateAsync(WebAppFactory.DhakaGadget, NewPhone(), hub, cod: 300);
        var toFirst = await DueAsync(hub, [first]);
        var toSecond = await DueAsync(hub, [second]);
        await PlanAsync("dhaka", hub);
        await StartAsync("dhaka", rider.UserId!.Value);
        var userId = rider.UserId.Value;
        var cash = dhaka.BaseDeliveryFee + 650;
        await DoorAsync("dhaka", door => door.HandOverAsync(userId, toFirst, [], cash, PaymentMethod.Cash, Cancel));
        var tripId = (await HubCashAsync("dhaka", hub))!.Trips.Single().Id;

        var stillOut = await HandInAsync("dhaka", hub, tripId, cash);
        await DoorAsync("dhaka", door => door.NotHomeAsync(userId, toSecond, Cancel));
        var staff = await SignInAsync("dhaka", "hub@dhaka.onedrop.test");
        var url = $"/Hub/Cash?hub={hub.Code}";
        var before = await staff.GetStringAsync(url, Cancel);
        var recorded = await staff.PostAsync(
            $"{url}&handler=HandIn",
            Form(before, ("trip", tripId.ToString()), ("received", (cash - 10).ToString(System.Globalization.CultureInfo.InvariantCulture))),
            Cancel);
        var after = await staff.GetStringAsync(url, Cancel);
        var again = await HandInAsync("dhaka", hub, tripId, cash);
        var riderPage = await (await SignInAsync("dhaka", rider.Email)).GetStringAsync("/Rider", Cancel);

        Assert.Equal("trip.cash.notBack", stillOut.Error!.Code);
        Assert.Contains("Cash in rider", before);
        Assert.Contains($"৳{cash:N0}", before);
        Assert.Contains("Record</button>", before);
        Assert.Equal(HttpStatusCode.Redirect, recorded.StatusCode);
        Assert.Contains("৳10 short", after);
        Assert.DoesNotContain("Record</button>", after);
        Assert.Equal("trip.cash.done", again.Error!.Code);
        var row = (await HubCashAsync("dhaka", hub))!.Trips.Single();
        Assert.Equal((cash, cash - 10, 10m, 0m), (row.Cash, row.HandedIn, row.Short, (await HubCashAsync("dhaka", hub))!.StillWithRiders));
        Assert.Matches($@"Cash handed in</dt>\s*<dd>\s*৳{cash - 10:N0}\s*of ৳{cash:N0}", riderPage);
        Assert.Null(await HubCashAsync("chattogram", hub));
        Assert.Equal(HttpStatusCode.NotFound, (await (await SignInAsync("chattogram", "hub@chattogram.onedrop.test")).GetAsync(url, Cancel)).StatusCode);
    }

    [Fact]
    public async Task Nothing_to_pay_at_the_door_when_the_advance_covers_the_fee_of_a_product_paid_online()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var hub = await NewHubAsync();
        var rider = await NewRiderAsync(hub, "Nothing due rider", new TripLoad(30, 25_000));
        var phone = NewPhone();
        var paidAhead = await CreateAsync(WebAppFactory.DhakaFashion, phone, hub, cod: 1000, feeInAdvance: true);
        var token = await AdvanceTokenAsync(paidAhead);
        await ConfirmAsync(handler => handler.RequestAdvanceAsync(token, PaymentMethod.Nagad, Cancel));
        factory.Services.GetRequiredService<FakePaymentLog>().Pay(Assert.Single(await AdvancesOfAsync(paidAhead)).GatewayReference!, DateTime.UtcNow);
        await ConfirmAsync(handler => handler.CheckAdvanceAsync(token, Cancel));
        var paidOnline = await CreateAsync(WebAppFactory.DhakaGadget, phone, hub);
        var delivery = await DueAsync(hub, [paidAhead, paidOnline]);
        await PlanAsync("dhaka", hub);
        await StartAsync("dhaka", rider.UserId!.Value);
        var userId = rider.UserId.Value;

        // The customer refuses the COD order: the advance already paid the fee of what is left, a product paid online
        var due = (await DoorAsync("dhaka", door => door.DueAsync(userId, delivery, [paidAhead], Cancel))).Value;
        var handedOver = await DoorAsync("dhaka", door => door.HandOverAsync(userId, delivery, [paidAhead], 0, PaymentMethod.Cash, Cancel));

        Assert.Equal((dhaka.BaseDeliveryFee, 0m), (due.PaidInAdvance, due.Total));
        Assert.Equal(new DoorResult(StopOutcome.Delivered, 0, BackToShop: false), handedOver.Value);
        Assert.Equal([OrderStatus.Refused, OrderStatus.Delivered], await StatusesAsync(paidAhead, paidOnline));
        Assert.Equal([new StopRow(StopOutcome.Delivered, 0, 0)], await StopsOfAsync(delivery));
        Assert.DoesNotContain(await PaymentsOfAsync(delivery), p => p.Purpose == PaymentPurpose.Door);
        Assert.Equal([(LedgerEntryKind.ReturnCharge, -dhaka.ReturnCharge)], (await LedgerOfAsync(paidAhead)).Select(line => (line.Kind, line.Amount)));
        Assert.Empty(await LedgerOfAsync(paidOnline));
    }

    /// <summary>Runs the settle job as it would run on <paramref name="day"/>, early in the morning.</summary>
    private async Task SettleAsync(DateOnly day)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById((await TenantAsync("dhaka")).TimeZone);
        var morning = day.ToDateTime(new TimeOnly(6, 0));
        var clock = new FakeTimeProvider(new DateTimeOffset(morning, timeZone.GetUtcOffset(morning)));
        await using var scope = await ScopeAsync("dhaka");

        await ActivatorUtilities.CreateInstance<SettleMerchantsJob>(scope.ServiceProvider, (TimeProvider)clock).SettleAsync(Cancel);
    }

    private async Task<HubCash?> HubCashAsync(string slug, TestHub hub)
    {
        await using var scope = await ScopeAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<HubCashHandler>().ListAsync(hub.Code, Cancel);
    }

    private async Task<Domain.Common.Result<CashTripRow>> HandInAsync(string slug, TestHub hub, long tripId, decimal received)
    {
        await using var scope = await ScopeAsync(slug);

        return await scope.ServiceProvider.GetRequiredService<HubCashHandler>().HandInAsync(hub.Code, tripId, received, Cancel);
    }

    /// <summary>The ledger lines of the orders, in the orders' order and then by kind.</summary>
    private async Task<IReadOnlyList<LedgerRow>> LedgerOfAsync(params string[] numbers)
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var lines = await (
            from entry in db.LedgerEntries
            join order in db.Orders on entry.OrderId equals order.Id
            where numbers.Contains(order.Number)
            select new { order.Number, Row = new LedgerRow(entry.Kind, entry.Amount, entry.PaymentId, entry.EntryDate, entry.SettlementId) })
            .ToListAsync(Cancel);

        return [.. lines.OrderBy(line => Array.IndexOf(numbers, line.Number)).ThenBy(line => line.Row.Kind).Select(line => line.Row)];
    }

    private async Task<decimal> CodOfPaymentAsync(long paymentId)
    {
        await using var scope = await ScopeAsync("dhaka");

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().LedgerEntries
            .Where(entry => entry.PaymentId == paymentId && entry.Kind == LedgerEntryKind.Cod)
            .SumAsync(entry => entry.Amount, Cancel);
    }

    private async Task<decimal> AmountOfSettlementAsync(long settlementId)
    {
        await using var scope = await ScopeAsync("dhaka");

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().LedgerEntries
            .Where(entry => entry.SettlementId == settlementId)
            .SumAsync(entry => entry.Amount, Cancel);
    }

    private async Task<IReadOnlyList<Settlement>> SettlementsOfAsync(long merchantId)
    {
        await using var scope = await ScopeAsync("dhaka");

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Settlements
            .Where(s => s.MerchantId == merchantId)
            .OrderBy(s => s.Id)
            .AsNoTracking()
            .ToListAsync(Cancel);
    }

    /// <summary>A shop of this test's own, picking up in Mirpur 10, with a login when asked for one.</summary>
    private async Task<TestShop> NewShopAsync(TestHub hub, bool withLogin = false)
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var name = $"Ledger Shop {hub.Code} {Guid.NewGuid():N}"[..28];
        var pickupArea = await db.Areas.SingleAsync(a => a.Name == "Mirpur 10", Cancel);
        var merchant = new Merchant(name, pickupArea.ZoneId, "01711999999", null);
        db.Merchants.Add(merchant);
        await db.SaveChangesAsync(Cancel);
        var (key, plaintext) = MerchantApiKey.Issue(merchant.Id, "Ledger test");
        db.PickupPoints.Add(new PickupPoint(merchant.Id, pickupArea.Id, "Shop", $"{name}, Mirpur 10", "01711999999", isDefault: true));
        db.MerchantApiKeys.Add(key);
        await db.SaveChangesAsync(Cancel);
        if (!withLogin)
        {
            return new TestShop(merchant.Id, plaintext, null);
        }

        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        var email = $"shop-{Guid.NewGuid():N}@dhaka.onedrop.test";
        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = name,
            TenantId = (await TenantAsync("dhaka")).Id,
            MerchantId = merchant.Id,
            Created = DateTime.UtcNow
        };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        Assert.True((await users.AddToRoleAsync(user, Roles.Merchant)).Succeeded);

        return new TestShop(merchant.Id, plaintext, email);
    }

    private sealed record LedgerRow(LedgerEntryKind Kind, decimal Amount, long? PaymentId, DateOnly EntryDate, long? SettlementId);

    private sealed record TestShop(long Id, string ApiKey, string? Email);
}
