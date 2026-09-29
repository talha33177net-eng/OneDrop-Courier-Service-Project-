using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Delivery.RiderDay;
using Domain.Common;
using Domain.Delivery;
using Domain.Grouping;
using Domain.Orders;
using Domain.Payments;
using Domain.Pricing;

namespace Application.Delivery.Door;

/// <summary>One order at the door, by shop.</summary>
public sealed record DoorOrder(string Number, string Shop);

/// <summary>A wallet payment shown to the customer as a QR on the rider's phone: <see cref="Link"/> is what it holds.</summary>
public sealed record DoorQr(PaymentMethod Method, decimal Amount, string Link);

/// <summary>
/// What the customer pays at a stop for the orders they take: the delivery fee on what is handed over (one fee for
/// the whole visit) and the shops' cash on delivery. Refused orders cost nothing and go back to their shops.
/// <see cref="Qr"/> is the wallet payment already shown for this amount, if any.
/// </summary>
public sealed record DoorDue(
    string Stop,
    IReadOnlyList<DoorOrder> Taking,
    IReadOnlyList<DoorOrder> Refusing,
    decimal Fee,
    decimal Cod,
    DoorQr? Qr = null)
{
    public decimal Total => Fee + Cod;
}

/// <summary>
/// How a stop ended. <see cref="BackToShop"/> is true when the parcels now go back to their shops (all refused, or
/// nobody home at the re-attempt) rather than wait at the hub for the free re-attempt. <see cref="Method"/> is how
/// the customer paid, null when nothing was paid.
/// </summary>
public sealed record DoorResult(StopOutcome Outcome, decimal Collected, bool BackToShop, PaymentMethod? Method = null);

/// <summary>
/// The rider at the door, for a trip that is out. A stop is one visit: every delivery for the customer in that area.
/// "No fee, no handover": the orders are handed over only when the customer has paid exactly what is due
/// (<see cref="DoorDue.Total"/>), worked out on what they take: in cash, which the rider confirms, or by bKash or
/// Nagad, which the gateway confirms after the customer scans the QR on the rider's phone. A payment keeps the fee and
/// the COD apart, and the SMS receipt follows it through the outbox. A refused order goes back to its shop. Nobody
/// home: the deliveries go back to the hub and out again on another day for free; nobody home a second time and their
/// orders go back to the shops.
/// </summary>
public class DoorHandler(IAppDbContext db, ITenantContext tenantContext, IPaymentGateway gateway, TimeProvider time)
{
    /// <summary>What is due at stop <paramref name="stopKey"/> when the customer refuses <paramref name="refused"/>.</summary>
    public async Task<Result<DoorDue>> DueAsync(
        long userId,
        string stopKey,
        IReadOnlyCollection<string> refused,
        CancellationToken cancellationToken = default)
    {
        var door = await OpenDoorAsync(userId, stopKey, cancellationToken);
        if (door.IsFailure)
        {
            return door.Error!;
        }

        return await DueAsync(door.Value, refused, cancellationToken);
    }

    /// <summary>
    /// Asks the wallet for <paramref name="amount"/>, which must be what is due now, and gives the QR for the customer
    /// to scan. Asking again for the same amount and wallet shows the same QR; an unpaid QR for another amount or
    /// wallet is dropped.
    /// </summary>
    public async Task<Result<DoorQr>> RequestQrAsync(
        long userId,
        string stopKey,
        IReadOnlyCollection<string> refused,
        PaymentMethod method,
        decimal amount,
        CancellationToken cancellationToken = default)
    {
        if (method is not (PaymentMethod.Bkash or PaymentMethod.Nagad))
        {
            return Error.Validation("door.qr.method", "A QR is for bKash or Nagad.");
        }

        var checkedDoor = await CheckAsync(userId, stopKey, refused, amount, cancellationToken);
        if (checkedDoor.IsFailure)
        {
            return checkedDoor.Error!;
        }

        var (door, due) = checkedDoor.Value;
        if (due.Taking.Count == 0)
        {
            return Error.Conflict("door.qr.nothing", "The customer refuses everything, so there is nothing to pay.");
        }

        if (due.Qr is { } shown && shown.Method == method)
        {
            return shown;
        }

        var dropped = await DropUnpaidAsync(door, keep: null, cancellationToken);
        if (dropped.IsFailure)
        {
            return dropped.Error!;
        }

        var payment = Payment.AtTheDoor(PaymentFor(door, method, due), time.GetUtcNow().UtcDateTime);
        var request = await gateway.RequestAsync(
            method,
            due.Total,
            Tenant.CurrencyCode,
            $"OneDrop delivery {door.Visit.Key}",
            cancellationToken);
        payment.RequestedAs(request.Reference, request.Link);
        db.Payments.Add(payment);
        await db.SaveChangesAsync(cancellationToken);

        return new DoorQr(method, payment.Amount, request.Link);
    }

    /// <summary>
    /// Hands over what the customer takes once they have paid <paramref name="collected"/>, which must be what is due
    /// now: a different amount means the page was out of date and nothing changes. Cash is paid when the rider says
    /// so; bKash or Nagad only once the gateway has the money for the QR shown.
    /// </summary>
    public async Task<Result<DoorResult>> HandOverAsync(
        long userId,
        string stopKey,
        IReadOnlyCollection<string> refused,
        decimal collected,
        PaymentMethod method,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(method))
        {
            return Error.Validation("door.method", "Say how the customer paid: cash, bKash or Nagad.");
        }

        var checkedDoor = await CheckAsync(userId, stopKey, refused, collected, cancellationToken);
        if (checkedDoor.IsFailure)
        {
            return checkedDoor.Error!;
        }

        var (door, due) = checkedDoor.Value;
        var now = time.GetUtcNow().UtcDateTime;
        var anyTaken = due.Taking.Count > 0;
        Payment? payment = null;
        if (anyTaken)
        {
            var paid = await PayAsync(door, due, method, now, cancellationToken);
            if (paid.IsFailure)
            {
                return paid.Error!;
            }

            payment = paid.Value;
        }

        var dropped = await DropUnpaidAsync(door, keep: payment, cancellationToken);
        if (dropped.IsFailure)
        {
            return dropped.Error!;
        }

        var shares = SharesOf(door.Visit, refused);
        foreach (var (delivery, share) in door.Visit.Deliveries.Zip(shares))
        {
            var taking = Out(delivery).Where(order => !refused.Contains(order.Number)).ToList();
            foreach (var order in Out(delivery).ToList())
            {
                order.MoveTo(
                    refused.Contains(order.Number) ? OrderStatus.Refused : OrderStatus.Delivered,
                    refused.Contains(order.Number) ? "Refused at the door; goes back to the shop" : "Handed to the customer");
            }

            delivery.Group.MoveTo(taking.Count > 0 ? DeliveryGroupStatus.Delivered : DeliveryGroupStatus.Cancelled, now);
            delivery.Stop.Complete(
                anyTaken ? StopOutcome.Delivered : StopOutcome.Refused,
                share,
                taking.Sum(order => order.CodAmount),
                payment,
                now);
        }

        return await SaveAsync(
            door,
            new DoorResult(
                anyTaken ? StopOutcome.Delivered : StopOutcome.Refused,
                due.Total,
                BackToShop: !anyTaken,
                payment?.Method),
            cancellationToken);
    }

    /// <summary>
    /// Nobody took the parcels. A delivery's first failed visit sends it back to the hub for a free re-attempt on
    /// another day (the parcels are scanned in again); at the second its orders go back to their shops.
    /// </summary>
    public async Task<Result<DoorResult>> NotHomeAsync(long userId, string stopKey, CancellationToken cancellationToken = default)
    {
        var opened = await OpenDoorAsync(userId, stopKey, cancellationToken);
        if (opened.IsFailure)
        {
            return opened.Error!;
        }

        var door = opened.Value;
        var dropped = await DropUnpaidAsync(door, keep: null, cancellationToken);
        if (dropped.IsFailure)
        {
            return dropped.Error!;
        }

        var now = time.GetUtcNow().UtcDateTime;
        var stopIds = door.Visit.Deliveries.Select(delivery => delivery.Stop.Id).ToList();
        var groupIds = door.Visit.Deliveries.Select(delivery => delivery.Group.Id).ToList();
        var failedBefore = await db.TripStops
            .Where(stop => groupIds.Contains(stop.DeliveryGroupId) &&
                stop.Outcome == StopOutcome.NotHome &&
                !stopIds.Contains(stop.Id))
            .Select(stop => stop.DeliveryGroupId)
            .ToListAsync(cancellationToken);

        var backToShop = false;
        foreach (var delivery in door.Visit.Deliveries)
        {
            var again = failedBefore.Contains(delivery.Group.Id);
            backToShop |= again;
            foreach (var order in Out(delivery).ToList())
            {
                order.MoveTo(
                    again ? OrderStatus.Refused : OrderStatus.AtHub,
                    again
                        ? "Nobody home at the re-attempt; goes back to the shop"
                        : "Nobody home; back to the hub for the free re-attempt");
            }

            delivery.Group.MoveTo(again ? DeliveryGroupStatus.Cancelled : DeliveryGroupStatus.Locked, now);
            delivery.Stop.Complete(StopOutcome.NotHome, 0, 0, null, now);
        }

        return await SaveAsync(door, new DoorResult(StopOutcome.NotHome, 0, backToShop), cancellationToken);
    }

    /// <summary>The rider's trip, out today, and its stop still to do named <paramref name="stopKey"/>.</summary>
    private async Task<Result<OpenDoor>> OpenDoorAsync(long userId, string stopKey, CancellationToken cancellationToken)
    {
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(Tenant.TimeZone);
        var (_, trip) = await db.TodaysTripAsync(userId, timeZone.LocalDay(time.GetUtcNow().UtcDateTime), cancellationToken);
        if (trip is null || trip.Status == TripStatus.Cancelled)
        {
            return RiderDayHandler.NoTrip;
        }

        if (trip.Status == TripStatus.Planned)
        {
            return Error.Conflict("door.notOut", "Start the trip first: the parcels are still at the hub.");
        }

        var visits = await db.VisitsAsync(trip.Id, cancellationToken);
        var visit = visits.FirstOrDefault(v => v.Key == stopKey);
        if (visit is null)
        {
            return Error.NotFound("door.stop", $"There is no stop {stopKey} on your trip.");
        }

        if (visit.Outcome is not null)
        {
            return TripStop.AlreadyDone;
        }

        var pending = await db.Payments
            .Where(p => p.TripId == trip.Id &&
                p.DeliveryGroupId == visit.Deliveries[0].Group.Id &&
                p.Status == PaymentStatus.Pending)
            .OrderByDescending(p => p.Id)
            .ToListAsync(cancellationToken);

        return new OpenDoor(trip, visit, visits, pending);
    }

    /// <summary>The open door and what is due there, when <paramref name="amount"/> is exactly that.</summary>
    private async Task<Result<(OpenDoor Door, DoorDue Due)>> CheckAsync(
        long userId,
        string stopKey,
        IReadOnlyCollection<string> refused,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var door = await OpenDoorAsync(userId, stopKey, cancellationToken);
        if (door.IsFailure)
        {
            return door.Error!;
        }

        var due = await DueAsync(door.Value, refused, cancellationToken);
        if (due.IsFailure)
        {
            return due.Error!;
        }

        if (amount != due.Value.Total)
        {
            return Error.Conflict(
                "door.amount",
                $"The amount to collect is {due.Value.Total:N0}, not {amount:N0}. Check the stop again.");
        }

        return (door.Value, due.Value);
    }

    private async Task<Result<DoorDue>> DueAsync(
        OpenDoor door,
        IReadOnlyCollection<string> refused,
        CancellationToken cancellationToken)
    {
        var carried = door.Visit.Deliveries.SelectMany(Out).ToList();
        if (refused.FirstOrDefault(number => carried.All(order => order.Number != number)) is { } unknown)
        {
            return Error.Validation("door.order", $"{unknown} is not an order you carry to this stop.");
        }

        var merchantIds = carried.Select(order => order.MerchantId).Distinct().ToList();
        var shops = await db.Merchants
            .Where(merchant => merchantIds.Contains(merchant.Id))
            .ToDictionaryAsync(merchant => merchant.Id, merchant => merchant.Name, cancellationToken);
        var taking = carried.Where(order => !refused.Contains(order.Number)).ToList();
        var fee = SharesOf(door.Visit, refused).Sum();
        var cod = taking.Sum(order => order.CodAmount);
        var shown = door.Pending.FirstOrDefault(p => p.Amount == fee + cod);

        return new DoorDue(
            door.Visit.Key,
            [.. taking.Select(order => new DoorOrder(order.Number, shops[order.MerchantId]))],
            [.. carried.Except(taking).Select(order => new DoorOrder(order.Number, shops[order.MerchantId]))],
            fee,
            cod,
            shown is null ? null : new DoorQr(shown.Method, shown.Amount, shown.PaymentLink!));
    }

    /// <summary>The paid payment for what is due: new cash, or the QR shown for it once the gateway has the money.</summary>
    private async Task<Result<Payment>> PayAsync(
        OpenDoor door,
        DoorDue due,
        PaymentMethod method,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (method == PaymentMethod.Cash)
        {
            var cash = Payment.AtTheDoor(PaymentFor(door, method, due), now);
            db.Payments.Add(cash);

            return cash;
        }

        var shown = door.Pending.FirstOrDefault(p => p.Method == method && p.Amount == due.Total);
        if (shown is null)
        {
            return Error.Conflict(
                "door.qr.none",
                $"Show the customer the {method.DisplayName()} QR for {due.Total:N0} first.");
        }

        if (!await gateway.IsPaidAsync(method, shown.GatewayReference!, cancellationToken))
        {
            return Error.Conflict(
                "door.qr.unpaid",
                $"The {method.DisplayName()} payment of {due.Total:N0} has not arrived yet. " +
                    "Ask the customer to finish paying, then check again.");
        }

        shown.MarkPaid(now);

        return shown;
    }

    /// <summary>
    /// Cancels the visit's QRs still pending, except <paramref name="keep"/>. One the customer has paid after all is
    /// never dropped: the rider must hand over what it paid for.
    /// </summary>
    private async Task<Result> DropUnpaidAsync(OpenDoor door, Payment? keep, CancellationToken cancellationToken)
    {
        foreach (var pending in door.Pending.Where(p => p != keep))
        {
            if (await gateway.IsPaidAsync(pending.Method, pending.GatewayReference!, cancellationToken))
            {
                return Error.Conflict(
                    "door.qr.paid",
                    $"The customer has already paid {pending.Amount:N0} by {pending.Method.DisplayName()}. " +
                        "Tick the same refusals again and hand over what that paid for.");
            }

            pending.Cancel();
        }

        return Result.Success();
    }

    private static DoorPayment PaymentFor(OpenDoor door, PaymentMethod method, DoorDue due)
    {
        return new DoorPayment(
            door.Visit.Deliveries[0].Group.CustomerId,
            door.Trip.Id,
            door.Trip.RiderId,
            door.Visit.Deliveries[0].Group.Id,
            method,
            due.Fee,
            due.Cod);
    }

    /// <summary>Each delivery's part of the visit's one fee, on the orders the customer takes.</summary>
    private IReadOnlyList<decimal> SharesOf(Visit visit, IReadOnlyCollection<string> refused)
    {
        return new DeliveryFeeCalculator(Tenant.Fees).VisitFees(
        [
            .. visit.Deliveries.Select(delivery => new FeeDelivery(
                delivery.Group.Kind,
                [.. Out(delivery).Where(order => !refused.Contains(order.Number)).Select(FeeLine.Of)]))
        ]);
    }

    /// <summary>Saves the door and finishes the trip once every stop is done.</summary>
    private async Task<Result<DoorResult>> SaveAsync(OpenDoor door, DoorResult result, CancellationToken cancellationToken)
    {
        if (door.Visits.All(visit => visit.Outcome is not null))
        {
            door.Trip.Finish();
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("door.changed", "This stop changed while you were at the door. Open it again.");
        }

        return result;
    }

    /// <summary>The delivery's orders out with the rider, still to hand over.</summary>
    private static IEnumerable<Order> Out(VisitDelivery delivery)
    {
        return delivery.Orders.Where(order => order.Status == OrderStatus.OutForDelivery);
    }

    private TenantInfo Tenant => tenantContext.Tenant ?? throw new InvalidOperationException("The door needs a tenant.");

    /// <summary>The rider's trip, the visit at the door, every visit of the trip and the visit's QRs still unpaid.</summary>
    private sealed record OpenDoor(Trip Trip, Visit Visit, IReadOnlyList<Visit> Visits, IReadOnlyList<Payment> Pending);
}
