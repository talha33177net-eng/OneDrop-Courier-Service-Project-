using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Application.Abstractions;
using Application.Grouping;
using Domain.Customers;
using Domain.Grouping;
using Domain.Merchants;
using Domain.Orders;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>
/// Task 3.4a: a fast delivery takes another shop's order while that shop's pickup route still runs by the delivery
/// day, and the checkout quote says the same. The grouping service runs against the real database with a fake clock
/// set around each shop's real route time.
/// </summary>
public class NextDayJoiningTests(WebAppFactory factory)
{
    // Monday 10:00 in Dhaka (UTC+6), a week of its own so no other test's deliveries are due around it
    private static readonly DateTimeOffset Monday = new(2026, 11, 2, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Tuesday = new(2026, 11, 3);

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_second_shop_joins_a_fast_delivery_before_its_pickup_route_leaves_on_the_delivery_day_and_not_after()
    {
        WebAppFactory.RequireDatabase();
        await using var scope = await ScopeForAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var fees = tenantContext.Tenant!.Fees;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(tenantContext.Tenant.TimeZone);
        var clock = new FakeTimeProvider(Monday);
        var grouping = new DeliveryGrouping(db, tenantContext, clock);
        var address = await NewAddressAsync(db);
        var shops = await db.PickupPoints.Where(p => p.IsDefault).OrderBy(p => p.Id).Take(3).ToListAsync(Cancel);
        var (fastShop, joiningShop, lateShop) = (shops[0], shops[1], shops[2]);

        var fast = await PlaceAsync(db, grouping, address, fastShop, DeliverySpeed.Fast);

        // Tuesday, half an hour before the second shop's route leaves
        clock.SetUtcNow(await OnTuesdayAsync(db, joiningShop, timeZone, TimeSpan.FromMinutes(-30)));
        var joiningQuote = await grouping.QuoteAsync(QuoteFor(address, joiningShop), Cancel);
        var joining = await PlaceAsync(db, grouping, address, joiningShop, DeliverySpeed.Combine);

        // Tuesday, half an hour after the third shop's route has left: its parcels would miss Tuesday's trip
        clock.SetUtcNow(await OnTuesdayAsync(db, lateShop, timeZone, TimeSpan.FromMinutes(30)));
        var lateQuote = await grouping.QuoteAsync(QuoteFor(address, lateShop), Cancel);
        var late = await PlaceAsync(db, grouping, address, lateShop, DeliverySpeed.Combine);

        var fastGroup = fast.DeliveryGroup!;
        Assert.Equal((DeliveryGroupStatus.Locked, DeliveryGroupKind.NextDay), (fastGroup.Status, fastGroup.Kind));
        Assert.Equal(fees.FastDeliveryFee, fast.AddedFee);

        Assert.Equal(new DeliveryQuote(fees.ExtraShopFee, JoinsDelivery: true), joiningQuote);
        Assert.Equal((fastGroup.Id, fees.ExtraShopFee), (joining.DeliveryGroupId, joining.AddedFee));

        Assert.Equal(new DeliveryQuote(fees.BaseDeliveryFee, JoinsDelivery: false), lateQuote);
        Assert.NotEqual(fastGroup.Id, late.DeliveryGroupId);
        Assert.Equal((DeliveryGroupStatus.Open, fees.BaseDeliveryFee), (late.DeliveryGroup!.Status, late.AddedFee));
    }

    [Fact]
    public async Task A_fast_order_placed_after_the_last_run_before_the_delivery_day_opens_its_own_next_day_delivery()
    {
        WebAppFactory.RequireDatabase();
        await using var scope = await ScopeForAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(tenantContext.Tenant!.TimeZone);
        var clock = new FakeTimeProvider(Monday);
        var grouping = new DeliveryGrouping(db, tenantContext, clock);
        var address = await NewAddressAsync(db);
        var shops = await db.PickupPoints.Where(p => p.IsDefault).OrderBy(p => p.Id).Take(2).ToListAsync(Cancel);

        var first = await PlaceAsync(db, grouping, address, shops[0], DeliverySpeed.Fast);
        clock.SetUtcNow(await OnTuesdayAsync(db, shops[1], timeZone, TimeSpan.FromMinutes(1)));
        var second = await PlaceAsync(db, grouping, address, shops[1], DeliverySpeed.Fast);

        Assert.NotEqual(first.DeliveryGroupId, second.DeliveryGroupId);
        Assert.Equal(DeliveryGroupKind.NextDay, second.DeliveryGroup!.Kind);
        Assert.Equal(tenantContext.Tenant.FastDeliveryFee, second.AddedFee);
    }

    /// <summary>The moment <paramref name="offset"/> from when the shop's pickup route leaves on Tuesday, in UTC.</summary>
    private static async Task<DateTimeOffset> OnTuesdayAsync(
        AppDbContext db,
        PickupPoint shop,
        TimeZoneInfo timeZone,
        TimeSpan offset)
    {
        var pickupTime = await (
            from area in db.Areas
            join route in db.PickupRoutes on area.ZoneId equals route.ZoneId
            where area.Id == shop.AreaId && !route.Archived
            select route.PickupTime)
            .SingleAsync(Cancel);
        var run = TimeZoneInfo.ConvertTimeToUtc(Tuesday.ToDateTime(pickupTime), timeZone);

        return new DateTimeOffset(run + offset, TimeSpan.Zero);
    }

    private static QuoteRequest QuoteFor(CustomerAddress address, PickupPoint shop)
    {
        return new QuoteRequest(
            shop.MerchantId,
            address.CustomerId,
            address.Id,
            shop.Id,
            DeliverySpeed.Combine,
            DoNotHold: false,
            WeightGrams: 500);
    }

    private static async Task<Order> PlaceAsync(
        AppDbContext db,
        DeliveryGrouping grouping,
        CustomerAddress address,
        PickupPoint shop,
        DeliverySpeed speed)
    {
        var hubId = await db.Areas.Where(a => a.Id == address.AreaId).Select(a => a.Zone!.HubId).SingleAsync(Cancel);
        var order = Order.Create(new NewOrder(
            shop.MerchantId,
            address.CustomerId,
            address.Id,
            shop.Id,
            "Next-day test",
            500,
            500,
            speed,
            false,
            [new NewPackage("Box", 500)])).Value;
        await grouping.SaveInGroupAsync(order, hubId, Cancel);

        return order;
    }

    private static async Task<CustomerAddress> NewAddressAsync(AppDbContext db)
    {
        var phone = "015" + Random.Shared.Next(0, 100_000_000).ToString("D8");
        var customer = new Customer(PhoneNumber.Parse(phone).Value, "Next-day test");
        db.Customers.Add(customer);
        await db.SaveChangesAsync(Cancel);

        var areaId = await db.Areas.Where(a => a.Name == "Mirpur 10").Select(a => a.Id).SingleAsync(Cancel);
        var address = new CustomerAddress(customer.Id, areaId, "House 5, Road 1", null, null);
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync(Cancel);

        return address;
    }

    private async Task<AsyncServiceScope> ScopeForAsync(string slug)
    {
        var tenant = await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync(slug);
        var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant!);

        return scope;
    }
}
