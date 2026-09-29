using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Application.Abstractions;
using Application.Grouping;
using Application.Notifications;
using Application.Notifications.SendOutbox;
using Domain.Customers;
using Domain.Grouping;
using Domain.Notifications;
using Domain.Orders;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;
using Infrastructure.Sms;

namespace Integration.Tests;

/// <summary>
/// Task 2.7: domain events go to the outbox in the same transaction as the change, and the sender texts the customer
/// and records the outcome. The test database keeps messages from earlier runs and from classes running in parallel,
/// and the sender takes the oldest first, so each test runs it until its own message has been handled.
/// </summary>
public class OutboxTests(WebAppFactory factory)
{
    private const string Area = "Mirpur 10";
    private const string Home = "House 5, Road 6";

    private readonly RecordingSms recorded = new();

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    [Fact]
    public async Task An_order_is_saved_with_its_message()
    {
        WebAppFactory.RequireDatabase();

        var order = await CreateAsync(WebAppFactory.DhakaFashion, NewPhone());

        var message = await OrderMessageAsync(order.OrderId);
        Assert.Equal(OutboxStatus.Pending, message.Status);
        Assert.Equal((await TenantAsync("dhaka")).Id, message.TenantId);
        Assert.Equal(0, message.Attempts);
    }

    [Fact]
    public async Task A_lock_that_loses_to_another_leaves_no_message_behind()
    {
        WebAppFactory.RequireDatabase();
        // March 2026: after the lock job tests' January clock, so their runs never lock this group
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 3, 2, 4, 0, 0, TimeSpan.Zero));
        var group = await PlaceAsync(clock, await NewAddressAsync());
        var late = group.LocksAt.AddHours(1);
        await using var first = await ScopeAsync("dhaka");
        await using var second = await ScopeAsync("dhaka");
        var firstDb = first.ServiceProvider.GetRequiredService<AppDbContext>();
        var secondDb = second.ServiceProvider.GetRequiredService<AppDbContext>();
        var byJob = await firstDb.DeliveryGroups.SingleAsync(g => g.Id == group.Id, Cancel);
        var byOrder = await secondDb.DeliveryGroups.SingleAsync(g => g.Id == group.Id, Cancel);

        byJob.LockIfDue(late);
        await firstDb.SaveChangesAsync(Cancel);
        byOrder.LockIfDue(late);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondDb.SaveChangesAsync(Cancel));

        await using var scope = await ScopeAsync("dhaka");
        var payload = JsonSerializer.Serialize(new DeliveryLockedMessage(group.Id));
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages
            .CountAsync(m => m.Type == nameof(DeliveryLockedMessage) && m.Payload == payload, Cancel));
        Assert.NotEmpty(byOrder.GetDomainEvents());
        Assert.Empty(byJob.GetDomainEvents());
    }

    [Fact]
    public async Task The_sender_texts_the_customer_which_delivery_each_order_joined_and_marks_it_sent()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var phone = NewPhone();
        var fashion = await CreateAsync(WebAppFactory.DhakaFashion, phone);
        var gadget = await CreateAsync(WebAppFactory.DhakaGadget, phone);
        var fast = await CreateAsync(WebAppFactory.DhakaBeauty, phone, speed: "fast");
        var group = await OpenGroupAsync(phone);
        var deliveryDay = LocalDate(dhaka, group.LocksAt);

        foreach (var order in new[] { fashion, gadget, fast })
        {
            var messageId = (await OrderMessageAsync(order.OrderId)).Id;
            await SendUntilAsync(messageId, message => message.Status == OutboxStatus.Sent, TimeProvider.System);
        }

        var texts = Texts(phone);
        Assert.All(texts, sms => Assert.Equal(dhaka.SmsSenderName, sms.Sender));
        Assert.Contains(
            $"Your Fashion House order {fashion.Number} is in OneDrop delivery {group.Number}. Orders from other shops " +
            $"can join it until the end of {Format(deliveryDay.AddDays(-1))}; we deliver on {Format(deliveryDay)}.",
            texts.Select(sms => sms.Text));
        Assert.Contains(texts, sms => sms.Text.StartsWith($"Your Gadget BD order {gadget.Number} is in OneDrop delivery {group.Number}."));
        Assert.Contains(
            texts,
            sms => sms.Text.StartsWith($"Your Beauty Shop order {fast.Number} is in OneDrop delivery DG-") &&
                sms.Text.EndsWith($", arriving on {Format(Today(dhaka).AddDays(1))}."));
        var sent = await OrderMessageAsync(fashion.OrderId);
        Assert.Equal(1, sent.Attempts);
        Assert.NotNull(sent.SentOn);
    }

    [Fact]
    public async Task Ship_now_texts_the_shops_and_the_new_delivery_day()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var phone = NewPhone();
        await CreateAsync(WebAppFactory.DhakaGadget, phone);
        await CreateAsync(WebAppFactory.DhakaFashion, phone);
        var group = await OpenGroupAsync(phone);

        await using (var scope = await ScopeAsync("dhaka"))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tracked = await db.DeliveryGroups.SingleAsync(g => g.Id == group.Id, Cancel);
            tracked.ShipNow(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(dhaka.TimeZone));
            await db.SaveChangesAsync(Cancel);
        }

        var lockMessage = await MessageAsync(nameof(DeliveryLockedMessage), new DeliveryLockedMessage(group.Id));
        await SendUntilAsync(lockMessage.Id, message => message.Status == OutboxStatus.Sent, TimeProvider.System);

        Assert.Contains(
            $"Your OneDrop delivery {group.Number} is closed. Your orders from Fashion House, Gadget BD arrive together " +
            $"on {Format(Today(dhaka).AddDays(1))}.",
            Texts(phone).Select(sms => sms.Text));
    }

    [Fact]
    public async Task A_failed_send_is_retried_after_1_2_4_and_8_minutes_then_given_up()
    {
        WebAppFactory.RequireDatabase();
        var phone = NewPhone();
        var order = await CreateAsync(WebAppFactory.DhakaFashion, phone);
        var messageId = (await OrderMessageAsync(order.OrderId)).Id;
        var clock = new FakeTimeProvider(DateTimeOffset.UtcNow.AddSeconds(1));
        var gateway = new GatewayDownFor(PhoneNumber.Parse(phone).Value.Value);
        var waits = new List<double>();

        for (var attempt = 1; attempt <= OutboxMessage.MaxAttempts; attempt++)
        {
            var failed = await SendUntilAsync(messageId, message => message.Attempts == attempt, clock, gateway);
            if (failed.Status == OutboxStatus.Pending)
            {
                // Not due again until the wait is over
                await SendOnceAsync(clock, gateway);
                Assert.Equal(attempt, (await FindMessageAsync(messageId)).Attempts);

                waits.Add((failed.NextAttemptOn!.Value - clock.GetUtcNow().UtcDateTime).TotalMinutes);
                clock.SetUtcNow(failed.NextAttemptOn.Value);
            }
        }

        var given = await FindMessageAsync(messageId);
        Assert.Equal([1, 2, 4, 8], waits);
        Assert.Equal(OutboxStatus.Failed, given.Status);
        Assert.Equal(GatewayDownFor.Error, given.LastError);
    }

    private static CancellationToken Cancel => TestContext.Current.CancellationToken;

    /// <summary>Runs the sender until <paramref name="done"/> holds for the message; returns the message then.</summary>
    private async Task<OutboxMessage> SendUntilAsync(
        long messageId,
        Func<OutboxMessage, bool> done,
        TimeProvider clock,
        ISmsSender? sms = null)
    {
        for (var run = 0; run < 20; run++)
        {
            await SendOnceAsync(clock, sms);
            var message = await FindMessageAsync(messageId);
            if (done(message))
            {
                return message;
            }
        }

        throw new InvalidOperationException($"Outbox message {messageId} was not handled after 20 runs.");
    }

    private async Task SendOnceAsync(TimeProvider clock, ISmsSender? sms)
    {
        await using var scope = await ScopeAsync("dhaka");
        var services = scope.ServiceProvider;
        var texts = ActivatorUtilities.CreateInstance<CustomerTexts>(services, sms ?? recorded);

        await new SendOutboxJob(services.GetRequiredService<AppDbContext>(), texts, clock, NullLogger<SendOutboxJob>.Instance)
            .RunAsync(Cancel);
    }

    private IReadOnlyList<SentSms> Texts(string phone)
    {
        var e164 = PhoneNumber.Parse(phone).Value.Value;

        return [.. recorded.Sent.Where(sms => sms.To == e164)];
    }

    private Task<OutboxMessage> OrderMessageAsync(long orderId)
    {
        return MessageAsync(nameof(OrderPlacedMessage), new OrderPlacedMessage(orderId));
    }

    private async Task<OutboxMessage> MessageAsync<TMessage>(string type, TMessage contract)
    {
        var payload = JsonSerializer.Serialize(contract);
        await using var scope = await ScopeAsync("dhaka");

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages
            .AsNoTracking()
            .SingleAsync(m => m.Type == type && m.Payload == payload, Cancel);
    }

    private async Task<OutboxMessage> FindMessageAsync(long id)
    {
        await using var scope = await ScopeAsync("dhaka");

        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().OutboxMessages
            .AsNoTracking()
            .SingleAsync(m => m.Id == id, Cancel);
    }

    /// <summary>
    /// A product paid online (no cash on delivery), so the order needs no confirmation from the customer (task 3.6b)
    /// and the text is the one about the delivery it joined.
    /// </summary>
    private async Task<Created> CreateAsync(string apiKey, string phone, string speed = "combine", decimal cod = 0)
    {
        var order = new
        {
            Customer = new { Name = "Outbox Customer", Phone = phone },
            Address = new { Area, Line1 = Home },
            Packages = new[] { new { Description = "Parcel", WeightGrams = 400 } },
            CodAmount = cod,
            Speed = speed
        };
        var response = await factory.ClientFor(apiKey).PostAsJsonAsync("/api/v1/orders", order, Json, Cancel);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Created>(Json, Cancel))!;
    }

    private async Task<DeliveryGroup> OpenGroupAsync(string phone)
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var e164 = PhoneNumber.Parse(phone).Value.Value;
        var customerId = await db.Customers.Where(c => c.Phone == e164).Select(c => c.Id).SingleAsync(Cancel);

        return await db.DeliveryGroups
            .AsNoTracking()
            .SingleAsync(g => g.CustomerId == customerId && g.Status == DeliveryGroupStatus.Open, Cancel);
    }

    /// <summary>An order from Dhaka's first shop to <paramref name="address"/>, placed at the clock's time.</summary>
    private async Task<DeliveryGroup> PlaceAsync(FakeTimeProvider clock, CustomerAddress address)
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var pickupPoint = await db.PickupPoints.Where(p => p.IsDefault).OrderBy(p => p.Id).FirstAsync(Cancel);
        var hubId = await db.Areas.Where(a => a.Id == address.AreaId).Select(a => a.Zone!.HubId).SingleAsync(Cancel);
        var order = Order.Create(new NewOrder(
            pickupPoint.MerchantId,
            address.CustomerId,
            address.Id,
            pickupPoint.Id,
            "Outbox test",
            500,
            500,
            DeliverySpeed.Combine,
            false,
            [new NewPackage("Box", 500)])).Value;
        await new DeliveryGrouping(db, scope.ServiceProvider.GetRequiredService<ITenantContext>(), clock)
            .SaveInGroupAsync(order, hubId, Cancel);

        return order.DeliveryGroup!;
    }

    private async Task<CustomerAddress> NewAddressAsync()
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var customer = new Customer(PhoneNumber.Parse(NewPhone()).Value, "Outbox test");
        db.Customers.Add(customer);
        await db.SaveChangesAsync(Cancel);

        var areaId = await db.Areas.Select(a => a.Id).FirstAsync(Cancel);
        var address = new CustomerAddress(customer.Id, areaId, Home, null, null);
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync(Cancel);

        return address;
    }

    private async Task<AsyncServiceScope> ScopeAsync(string slug)
    {
        var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(await TenantAsync(slug));

        return scope;
    }

    private async Task<TenantInfo> TenantAsync(string slug)
    {
        return (await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync(slug, Cancel))!;
    }

    private static DateOnly LocalDate(TenantInfo tenant, DateTime utc)
    {
        return DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone)));
    }

    private static DateOnly Today(TenantInfo tenant)
    {
        return LocalDate(tenant, DateTime.UtcNow);
    }

    private static string Format(DateOnly day)
    {
        return day.ToString("ddd d MMM", CultureInfo.InvariantCulture);
    }

    private static string NewPhone()
    {
        return "016" + Random.Shared.Next(0, 100_000_000).ToString("D8");
    }

    /// <summary>An SMS gateway that is down for one number and quietly accepts every other message.</summary>
    private sealed class GatewayDownFor(string phone) : ISmsSender
    {
        public const string Error = "SMS gateway unavailable";

        public Task SendAsync(PhoneNumber to, string senderName, string text, CancellationToken cancellationToken = default)
        {
            return to.Value == phone ? throw new HttpRequestException(Error) : Task.CompletedTask;
        }
    }

    /// <summary>Keeps every text this test's sender runs send; the app's SMS log holds only the last 50.</summary>
    private sealed class RecordingSms : ISmsSender
    {
        private readonly ConcurrentQueue<SentSms> sent = new();

        public IReadOnlyCollection<SentSms> Sent => sent;

        public Task SendAsync(PhoneNumber to, string senderName, string text, CancellationToken cancellationToken = default)
        {
            sent.Enqueue(new SentSms(DateTime.UtcNow, to.Value, senderName, text));

            return Task.CompletedTask;
        }
    }

    private sealed record Created(long OrderId, string Number, decimal Fee);
}
