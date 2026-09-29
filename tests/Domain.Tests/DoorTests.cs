using Domain.Common;
using Domain.Delivery;
using Domain.Grouping;
using Domain.Orders;
using Domain.Payments;
using Domain.Pricing;

namespace Domain.Tests;

/// <summary>Task 3.5: one fee per visit, orders left behind, refused parcels going back, and a stop's outcome.</summary>
public class DoorTests
{
    private const long Mirpur = 1;

    // The launch tenant's prices (seed 001, fast fee 003, weight 004)
    private static readonly FeeSchedule Dhaka = new(60, 25, 70, 2000, 15);
    private static readonly DateTime Now = new(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void One_delivery_at_a_visit_costs_its_own_fee()
    {
        var calculator = new DeliveryFeeCalculator(Dhaka);
        FeeLine[] lines = [Line(1), Line(2)];

        var shares = calculator.VisitFees([new FeeDelivery(DeliveryGroupKind.Waiting, lines)]);

        Assert.Equal([calculator.GroupFee(lines)], shares);
    }

    [Fact]
    public void Two_deliveries_at_one_door_cost_one_fee_split_between_them()
    {
        // A waiting delivery from shop 1 and a next-day one from shop 2 for the same customer and area: ৳60 + ৳25
        var shares = new DeliveryFeeCalculator(Dhaka).VisitFees(
        [
            new FeeDelivery(DeliveryGroupKind.Waiting, [Line(1)]),
            new FeeDelivery(DeliveryGroupKind.NextDay, [Line(2)])
        ]);

        Assert.Equal([60m, 25m], shares);
    }

    [Fact]
    public void A_shop_in_both_deliveries_is_paid_for_once()
    {
        var shares = new DeliveryFeeCalculator(Dhaka).VisitFees(
        [
            new FeeDelivery(DeliveryGroupKind.Waiting, [Line(1)]),
            new FeeDelivery(DeliveryGroupKind.Waiting, [Line(1), Line(2)])
        ]);

        Assert.Equal([60m, 25m], shares);
    }

    [Fact]
    public void A_follow_up_costs_the_extra_shop_fee_and_a_visit_is_priced_from_its_dearest_delivery()
    {
        var calculator = new DeliveryFeeCalculator(Dhaka);

        Assert.Equal(25, calculator.GroupFee([Line(3)], DeliveryGroupKind.FollowUp));

        // Listed follow-up first, the waiting delivery still sets the visit's first-shop fee
        Assert.Equal(
            [25m, 60m],
            calculator.VisitFees(
            [
                new FeeDelivery(DeliveryGroupKind.FollowUp, [Line(3)]),
                new FeeDelivery(DeliveryGroupKind.Waiting, [Line(1)])
            ]));
        Assert.Equal(
            [25m, 70m],
            calculator.VisitFees(
            [
                new FeeDelivery(DeliveryGroupKind.Waiting, [Line(1)]),
                new FeeDelivery(DeliveryGroupKind.ShippedNow, [Line(2)])
            ]));
    }

    [Fact]
    public void A_delivery_with_everything_refused_adds_nothing_to_the_visit()
    {
        var shares = new DeliveryFeeCalculator(Dhaka).VisitFees(
        [
            new FeeDelivery(DeliveryGroupKind.Waiting, [Line(1)]),
            new FeeDelivery(DeliveryGroupKind.NextDay, [])
        ]);

        Assert.Equal([60m, 0m], shares);
    }

    [Fact]
    public void A_follow_up_is_locked_at_once_for_the_next_day_and_can_still_be_joined()
    {
        var followUp = DeliveryGroup.FollowUp(new NewDeliveryGroup(1, 1, Mirpur, Now, TimeZoneInfo.Utc, JoinDays: 2));

        Assert.Equal((DeliveryGroupStatus.Locked, DeliveryGroupKind.FollowUp), (followUp.Status, followUp.Kind));
        Assert.Equal(Now, followUp.LockedOn);
        Assert.Equal(new DateTime(2026, 9, 29, 0, 0, 0, DateTimeKind.Utc), followUp.LocksAt);
        Assert.True(followUp.CanTake(Now, Now.AddHours(3), waits: true, TimeZoneInfo.Utc));
    }

    [Fact]
    public void An_order_left_behind_moves_to_a_later_delivery_keeping_the_fee_the_merchant_was_given()
    {
        var order = NewOrder();
        order.PlaceIn(DeliveryGroup.Open(new NewDeliveryGroup(1, 1, Mirpur, Now, TimeZoneInfo.Utc, 2)), 25);
        order.ClearDomainEvents();
        var followUp = DeliveryGroup.FollowUp(new NewDeliveryGroup(1, 1, Mirpur, Now, TimeZoneInfo.Utc, 2));

        order.FollowUpIn(followUp, Now);

        Assert.Same(followUp, order.DeliveryGroup);
        Assert.Equal((25m, Now, OrderStatus.Created), (order.AddedFee, order.LeftBehindOn!.Value, order.Status));
        Assert.Single(order.GetDomainEvents().OfType<OrderPlacedInDelivery>());
        Assert.Equal("Not ready when the rider left; goes in a later delivery", order.History[^1].Note);
    }

    [Fact]
    public void Only_an_order_still_waiting_is_left_behind_and_only_in_its_own_customers_delivery()
    {
        var followUp = DeliveryGroup.FollowUp(new NewDeliveryGroup(1, 1, Mirpur, Now, TimeZoneInfo.Utc, 2));
        var someoneElse = DeliveryGroup.FollowUp(new NewDeliveryGroup(2, 2, Mirpur, Now, TimeZoneInfo.Utc, 2));
        var outForDelivery = OutForDelivery();

        Assert.Throws<InvalidOperationException>(() => outForDelivery.FollowUpIn(followUp, Now));
        Assert.Throws<InvalidOperationException>(() => NewOrder().FollowUpIn(someoneElse, Now));
    }

    [Fact]
    public void A_refused_parcel_is_scanned_back_in_and_handed_back_to_its_shop_once()
    {
        var order = OutForDelivery();
        order.MoveTo(OrderStatus.Refused, "Refused at the door");

        var back = order.ReceiveAtHub(1, Mirpur, Now);
        var returned = order.ReturnToMerchant();
        var again = order.ReturnToMerchant();

        Assert.Equal(ScanOutcome.Recorded, back.Value);
        Assert.Equal(ScanOutcome.Recorded, returned.Value);
        Assert.Equal(ScanOutcome.AlreadyRecorded, again.Value);
        Assert.Equal(OrderStatus.ReturnedToMerchant, order.Status);
        Assert.Null(order.Packages[0].HubId);
    }

    [Fact]
    public void Only_an_order_the_customer_did_not_take_goes_back_to_the_shop()
    {
        var delivered = OutForDelivery();
        delivered.MoveTo(OrderStatus.Delivered);

        Assert.Equal("order.scan.return", delivered.ReturnToMerchant().Error!.Code);
        Assert.Equal("order.scan.return", NewOrder().ReturnToMerchant().Error!.Code);
    }

    [Fact]
    public void A_stop_is_done_once_and_collects_money_only_when_something_was_handed_over()
    {
        var stop = NewStop();
        var refused = NewStop();

        var cash = Payment.AtTheDoor(Door(PaymentMethod.Cash, 85, 1500), Now);

        Assert.True(stop.Complete(StopOutcome.Delivered, 85, 1500, cash, Now).IsSuccess);
        Assert.Equal(TripStop.AlreadyDone, stop.Complete(StopOutcome.NotHome, 0, 0, null, Now).Error);
        Assert.Equal(
            (StopOutcome.Delivered, 85m, 1500m, Now, cash),
            (stop.Outcome!.Value, stop.FeeCollected!.Value, stop.CodCollected!.Value, stop.CompletedOn!.Value, stop.Payment));
        Assert.Throws<InvalidOperationException>(() => refused.Complete(StopOutcome.Refused, 60, 0, null, Now));
    }

    [Fact]
    public void No_fee_no_handover_money_is_collected_only_with_a_paid_payment()
    {
        var unpaid = Payment.AtTheDoor(Door(PaymentMethod.Bkash, 85, 1500), Now);

        Assert.Throws<InvalidOperationException>(() => NewStop().Complete(StopOutcome.Delivered, 85, 1500, null, Now));
        Assert.Throws<InvalidOperationException>(() => NewStop().Complete(StopOutcome.Delivered, 85, 1500, unpaid, Now));
        Assert.Throws<InvalidOperationException>(
            () => NewStop().Complete(StopOutcome.NotHome, 0, 0, Payment.AtTheDoor(Door(PaymentMethod.Cash, 85, 0), Now), Now));

        // A second delivery of the visit hands over nothing of its own but was paid for with the visit
        var cash = Payment.AtTheDoor(Door(PaymentMethod.Cash, 85, 0), Now);
        Assert.True(NewStop().Complete(StopOutcome.Delivered, 0, 0, cash, Now).IsSuccess);
    }

    [Fact]
    public void Cash_is_paid_when_recorded_and_raises_the_receipt()
    {
        var cash = Payment.AtTheDoor(Door(PaymentMethod.Cash, 60, 1200), Now);

        Assert.Equal((PaymentStatus.Paid, Now, 1260m), (cash.Status, cash.PaidOn, cash.Amount));
        Assert.Equal(PaymentPurpose.Door, cash.Purpose);
        Assert.Same(cash, Assert.IsType<PaymentReceived>(Assert.Single(cash.GetDomainEvents())).Payment);
    }

    [Fact]
    public void A_wallet_payment_waits_for_the_gateway_and_is_paid_once()
    {
        var qr = Payment.AtTheDoor(Door(PaymentMethod.Nagad, 60, 1200), Now);
        qr.RequestedAs("NAGAD-1", "https://pay.fake/nagad/NAGAD-1");

        Assert.Equal((PaymentStatus.Pending, (DateTime?)null), (qr.Status, qr.PaidOn));
        Assert.Empty(qr.GetDomainEvents());
        Assert.Equal(("NAGAD-1", "https://pay.fake/nagad/NAGAD-1"), (qr.GatewayReference, qr.PaymentLink));
        Assert.Throws<InvalidOperationException>(() => qr.RequestedAs("NAGAD-2", "again"));

        Assert.True(qr.MarkPaid(Now).IsSuccess);
        Assert.True(qr.MarkPaid(Now.AddMinutes(1)).IsSuccess);
        Assert.Equal((PaymentStatus.Paid, Now), (qr.Status, qr.PaidOn!.Value));
        Assert.Single(qr.GetDomainEvents());
        Assert.Throws<InvalidOperationException>(qr.Cancel);
    }

    [Fact]
    public void A_cancelled_qr_cannot_be_paid_and_cash_needs_no_gateway()
    {
        var qr = Payment.AtTheDoor(Door(PaymentMethod.Bkash, 60, 0), Now);
        qr.Cancel();
        var cash = Payment.AtTheDoor(Door(PaymentMethod.Cash, 60, 0), Now);

        Assert.Equal("payment.cancelled", qr.MarkPaid(Now).Error!.Code);
        Assert.Equal(PaymentStatus.Cancelled, qr.Status);
        Assert.Throws<InvalidOperationException>(() => cash.RequestedAs("X", "Y"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Payment.AtTheDoor(Door(PaymentMethod.Cash, 0, 0), Now));
        Assert.Throws<ArgumentOutOfRangeException>(() => Payment.AtTheDoor(Door(PaymentMethod.Cash, -1, 100), Now));
    }

    private static DoorPayment Door(PaymentMethod method, decimal fee, decimal cod)
    {
        return new DoorPayment(CustomerId: 1, TripId: 1, RiderId: 1, DeliveryGroupId: 1, method, fee, cod);
    }

    [Fact]
    public void A_trip_finishes_only_once_it_is_out()
    {
        var trip = NewTrip();

        Assert.False(trip.Finish());
        trip.Start(Now);
        Assert.True(trip.Finish());
        Assert.Equal(TripStatus.Finished, trip.Status);
    }

    private static FeeLine Line(long merchantId)
    {
        return new FeeLine(merchantId, DeliverySpeed.Combine, OrderStatus.OutForDelivery, 500);
    }

    private static Order NewOrder()
    {
        return Order.Create(new NewOrder(
            MerchantId: 7,
            CustomerId: 1,
            AddressId: 1,
            PickupPointId: 1,
            RecipientName: "Rahim",
            CodAmount: 0,
            DeclaredValue: 0,
            Speed: DeliverySpeed.Combine,
            DoNotHold: false,
            Packages: [new NewPackage("Box", 500)])).Value;
    }

    private static Order OutForDelivery()
    {
        var order = NewOrder();
        order.ReceiveAtHub(1, Mirpur, Now);
        order.HandToRider(Mirpur);

        return order;
    }

    private static Trip NewTrip()
    {
        var phone = Customers.PhoneNumber.Parse("01722000001").Value;
        var rider = WithId(new Rider(Mirpur, "Rafiq", phone, new TripLoad(30, 25_000), null), 5);

        return WithId(Trip.Plan(rider, new DateOnly(2026, 9, 28)), 9);
    }

    private static TripStop NewStop()
    {
        var group = WithId(DeliveryGroup.OpenAlone(new NewDeliveryGroup(1, 1, Mirpur, Now, TimeZoneInfo.Utc, 2)), 30);

        return NewTrip().Add(group).Value;
    }

    /// <summary>Gives an entity the id the database would, so rules that need a saved entity can be tested.</summary>
    private static T WithId<T>(T entity, long id)
        where T : Entity
    {
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(entity, id);

        return entity;
    }
}
