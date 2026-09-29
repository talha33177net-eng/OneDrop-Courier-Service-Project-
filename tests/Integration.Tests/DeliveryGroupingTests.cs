using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Application.Abstractions;
using Application.Grouping;
using Domain.Customers;
using Domain.Grouping;
using Domain.Orders;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>
/// Week 2 "done when": orders from different shops for the same phone and address travel in one delivery group.
/// The day rules run the grouping service against the real database with a fake clock; the rest go through the API.
/// </summary>
public class DeliveryGroupingTests(WebAppFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    // Monday 10:00 in Dhaka (UTC+6). Day 3 starts on Wednesday at 00:00 Dhaka, Tuesday 18:00 UTC.
    private static readonly DateTimeOffset Monday = new(2026, 10, 5, 4, 0, 0, TimeSpan.Zero);
    private static readonly DateTime Day3 = new(2026, 10, 6, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Three_shops_orders_for_the_same_phone_and_address_travel_in_one_group()
    {
        WebAppFactory.RequireDatabase();
        var phone = NewPhone();

        var fashion = await CreateAsync(WebAppFactory.DhakaFashion, NewOrder(phone));
        var gadget = await CreateAsync(WebAppFactory.DhakaGadget, NewOrder(phone));
        var beauty = await CreateAsync(WebAppFactory.DhakaBeauty, NewOrder(phone));

        var groups = await GroupsOfAsync(fashion, gadget, beauty);
        var group = Assert.Single(groups.Distinct());
        Assert.Equal(DeliveryGroupStatus.Open, group.Status);
    }

    [Fact]
    public async Task Home_and_office_are_two_groups()
    {
        WebAppFactory.RequireDatabase();
        var phone = NewPhone();

        var home = await CreateAsync(WebAppFactory.DhakaFashion, NewOrder(phone));
        var office = await CreateAsync(
            WebAppFactory.DhakaGadget,
            NewOrder(phone) with { Address = new AddressBody("Gulshan 1", "Level 5, Tower 2, Road 90") });

        var groups = await GroupsOfAsync(home, office);
        Assert.Equal(2, groups.Distinct().Count());
        Assert.All(groups, group => Assert.Equal(DeliveryGroupStatus.Open, group.Status));
    }

    [Fact]
    public async Task Orders_for_a_new_customer_at_the_same_moment_open_one_group()
    {
        WebAppFactory.RequireDatabase();
        var phone = NewPhone();
        string[] keys = [WebAppFactory.DhakaFashion, WebAppFactory.DhakaGadget, WebAppFactory.DhakaBeauty];

        var orders = await Task.WhenAll(keys
            .SelectMany(key => Enumerable.Repeat(key, 3))
            .Select(key => CreateAsync(key, NewOrder(phone))));

        Assert.Single((await GroupsOfAsync(orders)).Distinct());
    }

    [Fact]
    public async Task Deliver_fast_opens_a_next_day_delivery_beside_the_open_group_and_later_orders_join_the_sooner_one()
    {
        WebAppFactory.RequireDatabase();
        var phone = NewPhone();

        var combined = await CreateAsync(WebAppFactory.DhakaFashion, NewOrder(phone));
        var fast = await CreateAsync(WebAppFactory.DhakaGadget, NewOrder(phone) with { Speed = "fast" });
        var food = await CreateAsync(WebAppFactory.DhakaBeauty, NewOrder(phone) with { DoNotHold = true });
        var later = await CreateAsync(WebAppFactory.DhakaGadget, NewOrder(phone));

        // The fast delivery leaves tomorrow and the Mirpur route still runs by then (whatever the time of day), so
        // the Don't hold order and the waiting one go with it rather than wait for the open group's Day 3
        var groups = await GroupsOfAsync(combined, fast, food, later);
        Assert.Equal(2, groups.Distinct().Count());
        Assert.Equal(DeliveryGroupStatus.Open, groups[0].Status);
        Assert.Equal((DeliveryGroupStatus.Locked, DeliveryGroupKind.NextDay), (groups[1].Status, groups[1].Kind));
        Assert.Equal(groups[1], groups[2]);
        Assert.Equal(groups[1], groups[3]);
    }

    [Fact]
    public async Task The_merchant_is_never_told_about_the_group()
    {
        WebAppFactory.RequireDatabase();
        var phone = NewPhone();
        await CreateAsync(WebAppFactory.DhakaFashion, NewOrder(phone));

        var response = await factory.ClientFor(WebAppFactory.DhakaGadget)
            .PostAsJsonAsync("/api/v1/orders", NewOrder(phone), Json, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.DoesNotContain("group", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DG-", body);
    }

    [Theory]
    [InlineData(0)] // Day 1, a moment later
    [InlineData(37 * 60 + 59)] // Day 2 (Tuesday), 23:59 Dhaka
    public async Task Orders_on_Day_1_and_Day_2_join_the_group(int minutesLater)
    {
        WebAppFactory.RequireDatabase();
        var clock = new FakeTimeProvider(Monday);
        var address = await NewAddressAsync();
        var first = await PlaceAsync(clock, address);

        clock.Advance(TimeSpan.FromMinutes(minutesLater));
        var second = await PlaceAsync(clock, address);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(Day3, first.LocksAt);
    }

    [Fact]
    public async Task An_order_on_Day_3_starts_a_new_group_and_the_old_one_locks()
    {
        WebAppFactory.RequireDatabase();
        var clock = new FakeTimeProvider(Monday);
        var address = await NewAddressAsync();
        var first = await PlaceAsync(clock, address);

        clock.SetUtcNow(Day3);
        var second = await PlaceAsync(clock, address);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(DeliveryGroupStatus.Open, second.Status);
        var old = await FindGroupAsync(first.Id);
        Assert.Equal(DeliveryGroupStatus.Locked, old.Status);
        Assert.Equal(Day3, old.LockedOn);
    }

    [Fact]
    public async Task The_join_days_are_the_tenants()
    {
        WebAppFactory.RequireDatabase();
        var clock = new FakeTimeProvider(Monday);
        var address = await NewAddressAsync();
        var tenant = await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync(
            "dhaka",
            TestContext.Current.CancellationToken);

        var group = await PlaceAsync(clock, address);

        Assert.Equal(new DateTime(2026, 10, 4, 18, 0, 0, DateTimeKind.Utc).AddDays(tenant!.GroupJoinDays), group.LocksAt);
    }

    [Fact]
    public async Task A_quote_joins_the_group_until_Day_3_then_prices_a_new_one_and_locks_nothing()
    {
        WebAppFactory.RequireDatabase();
        var cancellation = TestContext.Current.CancellationToken;
        var clock = new FakeTimeProvider(Monday);
        var address = await NewAddressAsync();
        var group = await PlaceAsync(clock, address);
        await using var scope = await ScopeForAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
        var inGroup = await db.Orders
            .Where(o => o.DeliveryGroupId == group.Id)
            .Select(o => o.MerchantId)
            .SingleAsync(cancellation);
        var otherShop = await db.PickupPoints
            .Where(p => p.MerchantId != inGroup && p.IsDefault)
            .Select(p => new { p.MerchantId, p.Id })
            .FirstAsync(cancellation);
        var grouping = new DeliveryGrouping(db, tenantContext, clock);
        var request = new QuoteRequest(
            otherShop.MerchantId,
            address.CustomerId,
            address.Id,
            otherShop.Id,
            DeliverySpeed.Combine,
            DoNotHold: false,
            WeightGrams: 1000);

        clock.SetUtcNow(Day3.AddSeconds(-1));
        var lastSecondOfDay2 = await grouping.QuoteAsync(request, cancellation);
        clock.SetUtcNow(Day3);
        var onDay3 = await grouping.QuoteAsync(request, cancellation);

        var fees = tenantContext.Tenant!.Fees;
        Assert.Equal(new DeliveryQuote(fees.ExtraShopFee, JoinsDelivery: true), lastSecondOfDay2);
        Assert.Equal(new DeliveryQuote(fees.BaseDeliveryFee, JoinsDelivery: false), onDay3);
        Assert.Equal(DeliveryGroupStatus.Open, (await FindGroupAsync(group.Id)).Status);
    }

    /// <summary>
    /// The race the unique index settles, made certain: as this order's new group is about to be inserted, a rival
    /// order commits a group for the same customer and address. The insert fails and the order joins the rival's.
    /// </summary>
    [Fact]
    public async Task An_order_that_loses_the_race_to_open_the_group_joins_the_winners()
    {
        WebAppFactory.RequireDatabase();
        var clock = new FakeTimeProvider(Monday);
        var address = await NewAddressAsync();
        DeliveryGroup? rival = null;
        var interceptor = new BeforeFirstSave(async () => rival = await PlaceAsync(clock, address));

        await using var scope = await ScopeForAsync("dhaka");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(WebAppFactory.ConnectionString)
            .AddInterceptors(scope.ServiceProvider.GetRequiredService<TenantSaveInterceptor>(), interceptor)
            .Options;
        await using var db = new AppDbContext(
            options,
            scope.ServiceProvider.GetRequiredService<ITenantContext>(),
            scope.ServiceProvider.GetRequiredService<ICurrentUser>());

        var loser = await PlaceAsync(clock, address, db, scope.ServiceProvider.GetRequiredService<ITenantContext>());

        Assert.NotNull(rival);
        Assert.Equal(rival.Id, loser.Id);
        Assert.Equal(1, await db.DeliveryGroups.CountAsync(
            g => g.CustomerId == address.CustomerId && g.Status == DeliveryGroupStatus.Open,
            TestContext.Current.CancellationToken));
    }

    private async Task<DeliveryGroup> PlaceAsync(FakeTimeProvider clock, CustomerAddress address)
    {
        await using var scope = await ScopeForAsync("dhaka");

        return await PlaceAsync(
            clock,
            address,
            scope.ServiceProvider.GetRequiredService<AppDbContext>(),
            scope.ServiceProvider.GetRequiredService<ITenantContext>());
    }

    private static async Task<DeliveryGroup> PlaceAsync(
        FakeTimeProvider clock,
        CustomerAddress address,
        AppDbContext db,
        ITenantContext tenantContext)
    {
        var cancellation = TestContext.Current.CancellationToken;
        var merchant = await db.PickupPoints.Where(p => p.IsDefault).OrderBy(p => p.Id).FirstAsync(cancellation);
        var hubId = await db.Areas
            .Where(a => a.Id == address.AreaId)
            .Select(a => a.Zone!.HubId)
            .SingleAsync(cancellation);
        var order = Order.Create(new NewOrder(
            merchant.MerchantId,
            address.CustomerId,
            address.Id,
            merchant.Id,
            "Group test",
            500,
            500,
            DeliverySpeed.Combine,
            false,
            [new NewPackage("Box", 500)])).Value;
        await new DeliveryGrouping(db, tenantContext, clock).SaveInGroupAsync(order, hubId, cancellation);

        return order.DeliveryGroup!;
    }

    private async Task<CustomerAddress> NewAddressAsync()
    {
        await using var scope = await ScopeForAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var customer = new Customer(PhoneNumber.Parse(NewPhone()).Value, "Group test");
        db.Customers.Add(customer);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var areaId = await db.Areas.Select(a => a.Id).FirstAsync(TestContext.Current.CancellationToken);
        var address = new CustomerAddress(customer.Id, areaId, "House 7, Road 3", null, null);
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return address;
    }

    private async Task<DeliveryGroup> FindGroupAsync(long id)
    {
        await using var scope = await ScopeForAsync("dhaka");

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().DeliveryGroups
            .SingleAsync(g => g.Id == id, TestContext.Current.CancellationToken);
    }

    /// <summary>Each order's group, in the order given.</summary>
    private async Task<GroupRow[]> GroupsOfAsync(params Created[] orders)
    {
        await using var scope = await ScopeForAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var numbers = orders.Select(o => o.Number).ToArray();
        var rows = await db.Orders
            .Where(o => numbers.Contains(o.Number))
            .Select(o => new
            {
                o.Number,
                Group = new GroupRow(o.DeliveryGroup!.Number, o.DeliveryGroup.Status, o.DeliveryGroup.Kind)
            })
            .ToDictionaryAsync(r => r.Number, r => r.Group, TestContext.Current.CancellationToken);

        return [.. numbers.Select(number => rows[number])];
    }

    private async Task<AsyncServiceScope> ScopeForAsync(string slug)
    {
        var tenant = await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync(slug);
        var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant!);

        return scope;
    }

    private async Task<Created> CreateAsync(string apiKey, OrderRequest order)
    {
        var response = await factory.ClientFor(apiKey).PostAsJsonAsync("/api/v1/orders", order, Json);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Created>(Json))!;
    }

    private static string NewPhone()
    {
        return "016" + Random.Shared.Next(0, 100_000_000).ToString("D8");
    }

    private static OrderRequest NewOrder(string phone)
    {
        return new OrderRequest(
            new CustomerBody("Group Customer", phone),
            new AddressBody("Mirpur 10", "House 12, Road 5"),
            [new PackageBody("Parcel", 400)],
            800);
    }

    private sealed record GroupRow(string Number, DeliveryGroupStatus Status, DeliveryGroupKind Kind);

    private sealed record OrderRequest(CustomerBody Customer, AddressBody Address, PackageBody[] Packages, decimal CodAmount)
    {
        public string Speed { get; init; } = "combine";

        public bool DoNotHold { get; init; }
    }

    private sealed record CustomerBody(string Name, string Phone);

    private sealed record AddressBody(string Area, string Line1);

    private sealed record PackageBody(string Description, int WeightGrams);

    private sealed record Created(string Number);

    /// <summary>Runs <paramref name="action"/> once, just before the context's first save reaches the database.</summary>
    private sealed class BeforeFirstSave(Func<Task> action) : SaveChangesInterceptor
    {
        private bool done;

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!done)
            {
                done = true;
                await action();
            }

            return result;
        }
    }
}
