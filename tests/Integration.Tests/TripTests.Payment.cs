using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Application.Delivery.Door;
using Application.Orders.ConfirmOrder;
using Application.Notifications;
using Application.Notifications.SendOutbox;
using Domain.Customers;
using Domain.Delivery;
using Domain.Orders;
using Domain.Payments;
using Infrastructure.Payments;
using Infrastructure.Persistence;
using Infrastructure.Sms;

namespace Integration.Tests;

/// <summary>Task 3.6a: paying at the door in cash or by a bKash or Nagad QR, and the receipt.</summary>
public partial class TripTests
{
    [Fact]
    public async Task A_wallet_payment_hands_over_only_once_the_gateway_has_the_money_and_the_customer_gets_a_receipt()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var hub = await NewHubAsync();
        var rider = await NewRiderAsync(hub, "Wallet rider", new TripLoad(30, 25_000));
        var phone = NewPhone();
        var fashion = await CreateAsync(WebAppFactory.DhakaFashion, phone, hub, cod: 1200);
        var gadget = await CreateAsync(WebAppFactory.DhakaGadget, phone, hub, cod: 800);
        var delivery = await DueAsync(hub, [fashion, gadget]);
        await PlanAsync("dhaka", hub);
        await StartAsync("dhaka", rider.UserId!.Value);
        var userId = rider.UserId.Value;
        var total = dhaka.BaseDeliveryFee + dhaka.ExtraShopFee + 2000;

        var qr = (await DoorAsync("dhaka", door => door.RequestQrAsync(userId, delivery, [], PaymentMethod.Bkash, total, Cancel))).Value;
        var sameAgain = (await DoorAsync("dhaka", door => door.RequestQrAsync(userId, delivery, [], PaymentMethod.Bkash, total, Cancel))).Value;
        var shown = (await DoorAsync("dhaka", door => door.DueAsync(userId, delivery, [], Cancel))).Value;
        var early = await DoorAsync("dhaka", door => door.HandOverAsync(userId, delivery, [], total, PaymentMethod.Bkash, Cancel));
        var otherWallet = await DoorAsync("dhaka", door => door.HandOverAsync(userId, delivery, [], total, PaymentMethod.Nagad, Cancel));

        // One QR for the amount, shown again on the stop; nothing handed over before the money arrives
        Assert.Equal(new DoorQr(PaymentMethod.Bkash, total, qr.Link), sameAgain);
        Assert.Equal(qr, shown.Qr);
        Assert.Equal("door.qr.unpaid", early.Error!.Code);
        Assert.Equal("door.qr.none", otherWallet.Error!.Code);
        Assert.Equal([OrderStatus.OutForDelivery, OrderStatus.OutForDelivery], await StatusesAsync(fashion, gadget));
        var pending = Assert.Single(await PaymentsOfAsync(delivery));
        Assert.Equal(
            (PaymentMethod.Bkash, PaymentStatus.Pending, dhaka.BaseDeliveryFee + dhaka.ExtraShopFee, 2000m),
            (pending.Method, pending.Status, pending.Fee, pending.Cod));

        // The customer pays in the app. A refusal now would change the amount they paid for: refused, not dropped
        factory.Services.GetRequiredService<FakePaymentLog>().Pay(pending.GatewayReference!, DateTime.UtcNow);
        var afterPaying = dhaka.BaseDeliveryFee + 1200;
        var changedMind = await DoorAsync("dhaka", door => door.HandOverAsync(userId, delivery, [gadget], afterPaying, PaymentMethod.Cash, Cancel));
        var handedOver = await DoorAsync("dhaka", door => door.HandOverAsync(userId, delivery, [], total, PaymentMethod.Bkash, Cancel));
        var today = (await RiderTodayAsync("dhaka", userId))!;

        Assert.Equal("door.qr.paid", changedMind.Error!.Code);
        Assert.Equal(new DoorResult(StopOutcome.Delivered, total, BackToShop: false, PaymentMethod.Bkash), handedOver.Value);
        Assert.Equal([OrderStatus.Delivered, OrderStatus.Delivered], await StatusesAsync(fashion, gadget));
        var paid = Assert.Single(await PaymentsOfAsync(delivery));
        Assert.Equal((pending.Id, PaymentStatus.Paid), (paid.Id, paid.Status));
        Assert.NotNull(paid.PaidOn);
        Assert.Equal([paid.Id], await StopPaymentIdsAsync(delivery));
        Assert.Equal((total, 0m, PaymentMethod.Bkash), (today.Collected, today.Cash, today.Stops.Single().PaidBy));

        // The receipt, written from the payment and what was handed over
        var receipt = await ReceiptAsync(paid.Id);
        Assert.Equal(PhoneNumber.Parse(phone).Value.Value, receipt.To);
        Assert.Equal(
            $"OneDrop receipt: ৳{total:N0} paid by bKash on {LocalDay(paid.PaidOn!.Value, dhaka)} for delivery " +
                $"{delivery}. Delivery fee ৳{dhaka.BaseDeliveryFee + dhaka.ExtraShopFee:N0}. " +
                $"Fashion House {fashion} ৳1,200, Gadget BD {gadget} ৳800. Thank you.",
            receipt.Text);
    }

    [Fact]
    public async Task Changing_wallet_or_paying_cash_drops_the_unpaid_qr_and_cash_is_counted_for_the_hub()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var hub = await NewHubAsync();
        var rider = await NewRiderAsync(hub, "Cash rider", new TripLoad(30, 25_000));
        var paying = await CreateAsync(WebAppFactory.DhakaBeauty, NewPhone(), hub, cod: 1500);
        var refusing = await CreateAsync(WebAppFactory.DhakaGadget, NewPhone(), hub, cod: 900);
        var payingDelivery = await DueAsync(hub, [paying]);
        var refusingDelivery = await DueAsync(hub, [refusing]);
        await PlanAsync("dhaka", hub);
        await StartAsync("dhaka", rider.UserId!.Value);
        var userId = rider.UserId.Value;
        var total = dhaka.BaseDeliveryFee + 1500;

        var noQrForCash = await DoorAsync("dhaka", door => door.RequestQrAsync(userId, payingDelivery, [], PaymentMethod.Cash, total, Cancel));
        var noMethod = await DoorAsync("dhaka", door => door.HandOverAsync(userId, payingDelivery, [], total, (PaymentMethod)9, Cancel));
        var wrongAmount = await DoorAsync("dhaka", door => door.RequestQrAsync(userId, payingDelivery, [], PaymentMethod.Bkash, 100, Cancel));
        await DoorAsync("dhaka", door => door.RequestQrAsync(userId, payingDelivery, [], PaymentMethod.Bkash, total, Cancel));
        await DoorAsync("dhaka", door => door.RequestQrAsync(userId, payingDelivery, [], PaymentMethod.Nagad, total, Cancel));
        var switched = await PaymentsOfAsync(payingDelivery);
        var cash = await DoorAsync("dhaka", door => door.HandOverAsync(userId, payingDelivery, [], total, PaymentMethod.Cash, Cancel));

        Assert.Equal("door.qr.method", noQrForCash.Error!.Code);
        Assert.Equal("door.method", noMethod.Error!.Code);
        Assert.Equal("door.amount", wrongAmount.Error!.Code);
        Assert.Equal(
            [(PaymentMethod.Bkash, PaymentStatus.Cancelled), (PaymentMethod.Nagad, PaymentStatus.Pending)],
            switched.Select(p => (p.Method, p.Status)));
        Assert.Equal(PaymentMethod.Cash, cash.Value.Method);
        var payments = await PaymentsOfAsync(payingDelivery);
        Assert.Equal(
            [
                (PaymentMethod.Bkash, PaymentStatus.Cancelled),
                (PaymentMethod.Nagad, PaymentStatus.Cancelled),
                (PaymentMethod.Cash, PaymentStatus.Paid)
            ],
            payments.Select(p => (p.Method, p.Status)));
        Assert.Equal([payments[2].Id], await StopPaymentIdsAsync(payingDelivery));

        // Everything refused after a QR was shown: the QR is dropped and nothing is paid
        var refusingTotal = dhaka.BaseDeliveryFee + 900;
        await DoorAsync("dhaka", door => door.RequestQrAsync(userId, refusingDelivery, [], PaymentMethod.Nagad, refusingTotal, Cancel));
        var refused = await DoorAsync("dhaka", door => door.HandOverAsync(userId, refusingDelivery, [refusing], 0, PaymentMethod.Cash, Cancel));
        var today = (await RiderTodayAsync("dhaka", userId))!;

        Assert.Equal(new DoorResult(StopOutcome.Refused, 0, BackToShop: true), refused.Value);
        Assert.Equal([PaymentStatus.Cancelled], (await PaymentsOfAsync(refusingDelivery)).Select(p => p.Status));
        Assert.Equal([(long?)null], await StopPaymentIdsAsync(refusingDelivery));
        Assert.Equal((total, total), (today.Collected, today.Cash));
        Assert.Equal(
            [PaymentMethod.Cash, null],
            today.Stops.OrderBy(stop => stop.Key == payingDelivery ? 0 : 1).Select(stop => stop.PaidBy));
    }

    [Fact]
    public async Task The_rider_shows_the_qr_on_the_page_and_hands_over_after_checking()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var hub = await NewHubAsync();
        var rider = await NewRiderAsync(hub, "Page rider", new TripLoad(30, 25_000));
        var order = await CreateAsync(WebAppFactory.DhakaFashion, NewPhone(), hub, cod: 700);
        var delivery = await DueAsync(hub, [order]);
        await PlanAsync("dhaka", hub);
        await StartAsync("dhaka", rider.UserId!.Value);
        var client = await SignInAsync("dhaka", rider.Email);
        var total = dhaka.BaseDeliveryFee + 700;
        var amount = total.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var review = await client.GetStringAsync($"/Rider?stop={delivery}", Cancel);
        var asked = await client.PostAsync(
            "/Rider?handler=Qr",
            Form(review, ("stop", delivery), ("collected", amount), ("method", nameof(PaymentMethod.Nagad))),
            Cancel);
        var withQr = await client.GetStringAsync(asked.Headers.Location!.OriginalString, Cancel);
        var tooEarly = await client.PostAsync(
            "/Rider?handler=HandOver",
            Form(withQr, ("stop", delivery), ("collected", amount), ("method", nameof(PaymentMethod.Nagad))),
            Cancel);
        var stillAtTheDoor = await client.GetStringAsync(tooEarly.Headers.Location!.OriginalString, Cancel);
        var reference = Assert.Single(await PaymentsOfAsync(delivery)).GatewayReference!;
        factory.Services.GetRequiredService<FakePaymentLog>().Pay(reference, DateTime.UtcNow);
        var checkedPayment = await client.PostAsync(
            "/Rider?handler=HandOver",
            Form(stillAtTheDoor, ("stop", delivery), ("collected", amount), ("method", nameof(PaymentMethod.Nagad))),
            Cancel);
        var done = await client.GetStringAsync(checkedPayment.Headers.Location!.OriginalString, Cancel);

        Assert.Equal($"/Rider?stop={delivery}#door", asked.Headers.Location!.OriginalString);
        Assert.Contains("<svg", withQr);
        Assert.Contains($"৳{total:N0} by Nagad", withQr);
        Assert.Contains("Check payment and hand over</button>", withQr);
        Assert.Contains("has not arrived yet", stillAtTheDoor);
        Assert.Contains("Check payment and hand over</button>", stillAtTheDoor);
        Assert.Equal("/Rider", checkedPayment.Headers.Location!.OriginalString);
        Assert.Contains("by Nagad.", done);
        Assert.Contains("Delivered, paid by Nagad", done);
        Assert.Matches(@"Cash to hand in</dt>\s*<dd>৳0</dd>", done);
        Assert.Equal([OrderStatus.Delivered], await StatusesAsync(order));
    }

    [Fact]
    public async Task A_fee_paid_in_advance_is_not_collected_again_at_the_door()
    {
        WebAppFactory.RequireDatabase();
        var dhaka = await TenantAsync("dhaka");
        var hub = await NewHubAsync();
        var rider = await NewRiderAsync(hub, "Advance rider", new TripLoad(30, 25_000));
        var phone = NewPhone();
        var paidAhead = await CreateAsync(WebAppFactory.DhakaFashion, phone, hub, cod: 1000, feeInAdvance: true);
        var token = await AdvanceTokenAsync(paidAhead);

        // The customer pays the delivery's first-shop fee before the parcel leaves the shop (3.6b)
        await ConfirmAsync(handler => handler.RequestAdvanceAsync(token, PaymentMethod.Bkash, Cancel));
        var advance = Assert.Single(await AdvancesOfAsync(paidAhead));
        factory.Services.GetRequiredService<FakePaymentLog>().Pay(advance.GatewayReference!, DateTime.UtcNow);
        await ConfirmAsync(handler => handler.CheckAdvanceAsync(token, Cancel));

        // A second shop joins the same delivery: its extra-shop fee is still paid at the door
        var joining = await CreateAsync(WebAppFactory.DhakaGadget, phone, hub, cod: 500);
        var delivery = await DueAsync(hub, [paidAhead, joining]);
        await PlanAsync("dhaka", hub);
        await StartAsync("dhaka", rider.UserId!.Value);
        var userId = rider.UserId.Value;

        var stopBefore = Assert.Single((await RiderTodayAsync("dhaka", userId))!.Stops);
        var due = (await DoorAsync("dhaka", door => door.DueAsync(userId, delivery, [], Cancel))).Value;
        var handedOver = await DoorAsync("dhaka", door => door.HandOverAsync(userId, delivery, [], due.Total, PaymentMethod.Cash, Cancel));
        var stop = Assert.Single(await StopsOfAsync(delivery));

        // The base fee was paid ahead, so only the extra shop and the COD are left at the door
        Assert.Equal((dhaka.BaseDeliveryFee, dhaka.ExtraShopFee, 1500m), (due.PaidInAdvance, due.Fee, due.Cod));

        // The rider's stop shows the same, not the fee the whole delivery cost
        Assert.Equal((dhaka.ExtraShopFee, 1500m), (stopBefore.Fee, stopBefore.Cod));
        Assert.Equal(dhaka.ExtraShopFee + 1500, due.Total);
        Assert.Equal(StopOutcome.Delivered, handedOver.Value.Outcome);
        Assert.Equal(new StopRow(StopOutcome.Delivered, dhaka.ExtraShopFee, 1500), stop);

        // The advance is a payment of its own and stays paid
        Assert.Equal(
            [(PaymentPurpose.Advance, PaymentStatus.Paid, dhaka.BaseDeliveryFee)],
            (await AdvancesOfAsync(paidAhead)).Select(p => (p.Purpose, p.Status, p.Fee)));
    }

    private async Task<T> ConfirmAsync<T>(Func<ConfirmOrderHandler, Task<T>> act)
    {
        await using var scope = await ScopeAsync("dhaka");

        return await act(scope.ServiceProvider.GetRequiredService<ConfirmOrderHandler>());
    }

    private async Task<string> AdvanceTokenAsync(string number)
    {
        await using var scope = await ScopeAsync("dhaka");

        return (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders
            .AsNoTracking()
            .SingleAsync(order => order.Number == number, Cancel))
            .CustomerToken!;
    }

    /// <summary>The advances paid for the order's delivery, oldest first.</summary>
    private async Task<IReadOnlyList<Payment>> AdvancesOfAsync(string number)
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await (
            from payment in db.Payments
            join order in db.Orders on payment.DeliveryGroupId equals order.DeliveryGroupId
            where order.Number == number && payment.Purpose == PaymentPurpose.Advance
            orderby payment.Id
            select payment)
            .AsNoTracking()
            .ToListAsync(Cancel);
    }

    private static FormUrlEncodedContent Form(string page, params (string Name, string Value)[] fields)
    {
        return new FormUrlEncodedContent(
        [
            .. fields.Select(field => KeyValuePair.Create(field.Name, field.Value)),
            KeyValuePair.Create("__RequestVerificationToken", Token().Match(page).Groups[1].Value)
        ]);
    }

    /// <summary>The payments made at a delivery's visit, oldest first.</summary>
    private async Task<IReadOnlyList<Payment>> PaymentsOfAsync(string delivery)
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await (
            from payment in db.Payments
            join g in db.DeliveryGroups on payment.DeliveryGroupId equals g.Id
            where g.Number == delivery
            orderby payment.Id
            select payment)
            .AsNoTracking()
            .ToListAsync(Cancel);
    }

    private async Task<long?[]> StopPaymentIdsAsync(string delivery)
    {
        await using var scope = await ScopeAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await (
            from stop in db.TripStops
            join g in db.DeliveryGroups on stop.DeliveryGroupId equals g.Id
            where g.Number == delivery
            select stop.PaymentId)
            .ToArrayAsync(Cancel);
    }

    /// <summary>Writes the payment's receipt from its outbox row, as the sender would, into a sender of its own.</summary>
    private async Task<SentSms> ReceiptAsync(long paymentId)
    {
        await using var scope = await ScopeAsync("dhaka");
        var services = scope.ServiceProvider;
        var payload = JsonSerializer.Serialize(new PaymentReceivedMessage(paymentId));
        var message = await services.GetRequiredService<AppDbContext>().OutboxMessages
            .SingleAsync(m => m.Type == nameof(PaymentReceivedMessage) && m.Payload == payload, Cancel);
        var sms = new RecordingSms();
        await ActivatorUtilities.CreateInstance<CustomerTexts>(services, (ISmsSender)sms).SendAsync(message, Cancel);

        return Assert.Single(sms.Sent);
    }

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

    private static string LocalDay(DateTime utc, TenantInfo tenant)
    {
        return TimeZoneInfo.ConvertTimeFromUtc(utc, TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone))
            .ToString("ddd d MMM", System.Globalization.CultureInfo.InvariantCulture);
    }
}
