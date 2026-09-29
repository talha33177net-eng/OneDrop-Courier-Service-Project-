using Domain.Common;
using Domain.Delivery;
using Domain.Grouping;
using Domain.Orders;
using Domain.Payments;

namespace Domain.Tests;

/// <summary>Task 3.7: the shops' ledger, their payouts with charges carried forward, and the rider's cash hand-in.</summary>
public class LedgerTests
{
    private const long Mirpur = 1;
    private const long Shop = 7;
    private const string Account = "+8801711000007";

    private static readonly DateTime Now = new(2026, 9, 29, 15, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Monday = new(2026, 9, 28);
    private static readonly DateOnly Tuesday = new(2026, 9, 29);

    [Fact]
    public void A_delivered_orders_cod_is_owed_to_its_shop_from_the_payment_it_came_in()
    {
        var order = Delivered(cod: 1200);
        var payment = Paid(cod: 1200);

        var entry = LedgerEntry.Cod(order, payment, Monday);

        Assert.Equal((Shop, LedgerEntryKind.Cod, 1200m, Monday), (entry.MerchantId, entry.Kind, entry.Amount, entry.EntryDate));
        Assert.Same(payment, entry.Payment);
        Assert.False(entry.IsSettled);
    }

    [Fact]
    public void Cod_is_owed_only_for_a_delivered_order_with_money_and_a_paid_payment()
    {
        var pending = Payment.AtTheDoor(new DoorPayment(1, 1, 1, 1, PaymentMethod.Bkash, 60, 1200), Now);

        Assert.Throws<InvalidOperationException>(() => LedgerEntry.Cod(OutForDelivery(cod: 1200), Paid(cod: 1200), Monday));
        Assert.Throws<InvalidOperationException>(() => LedgerEntry.Cod(Delivered(cod: 1200), pending, Monday));
        Assert.Throws<ArgumentOutOfRangeException>(() => LedgerEntry.Cod(Delivered(cod: 0), Paid(cod: 0), Monday));
    }

    [Fact]
    public void A_return_and_a_late_handover_are_charged_to_the_shop()
    {
        var returned = LedgerEntry.ReturnCharge(Refused(), 30, Monday);
        var late = LedgerEntry.LateHandoverFee(LeftBehind(), 25, Monday);

        Assert.Equal((LedgerEntryKind.ReturnCharge, -30m, Shop), (returned.Kind, returned.Amount, returned.MerchantId));
        Assert.Equal((LedgerEntryKind.LateHandoverFee, -25m, Shop), (late.Kind, late.Amount, late.MerchantId));
        Assert.Null(returned.Payment);
    }

    [Fact]
    public void Only_an_order_going_back_pays_a_return_and_only_one_left_behind_a_late_fee()
    {
        Assert.Throws<InvalidOperationException>(() => LedgerEntry.ReturnCharge(Delivered(cod: 0), 30, Monday));
        Assert.Throws<InvalidOperationException>(() => LedgerEntry.LateHandoverFee(NewOrder(0), 25, Monday));
        Assert.Throws<ArgumentOutOfRangeException>(() => LedgerEntry.ReturnCharge(Refused(), 0, Monday));
        Assert.Throws<ArgumentOutOfRangeException>(() => LedgerEntry.LateHandoverFee(LeftBehind(), -5, Monday));
    }

    [Fact]
    public void A_payout_is_the_shops_cod_less_its_charges_and_takes_every_line()
    {
        LedgerEntry[] lines = [Cod(1200, Monday), Cod(800, Monday), LedgerEntry.ReturnCharge(Refused(), 30, Monday)];

        var settlement = Settlement.Of(Shop, Account, Monday, lines)!;

        Assert.Equal((Shop, Monday, 1970m, SettlementStatus.Pending, Account), (settlement.MerchantId, settlement.UpToDate, settlement.Amount, settlement.Status, settlement.Account));
        Assert.All(lines, line => Assert.Same(settlement, line.Settlement));
    }

    [Fact]
    public void Charges_the_days_cod_does_not_cover_wait_and_come_off_the_next_payout()
    {
        LedgerEntry[] monday = [LedgerEntry.ReturnCharge(Refused(), 30, Monday), LedgerEntry.LateHandoverFee(LeftBehind(), 25, Monday)];

        var nothing = Settlement.Of(Shop, Account, Monday, monday);
        var tuesday = Settlement.Of(Shop, Account, Tuesday, [.. monday, Cod(500, Tuesday)])!;

        Assert.Null(nothing);
        Assert.Equal(445m, tuesday.Amount);
        Assert.All(monday, line => Assert.Same(tuesday, line.Settlement));
    }

    [Fact]
    public void A_payout_takes_only_the_shops_own_lines_not_yet_paid_up_to_its_day()
    {
        var paid = Cod(100, Monday);
        Settlement.Of(Shop, Account, Monday, [paid]);

        Assert.Throws<InvalidOperationException>(() => Settlement.Of(Shop, Account, Tuesday, [paid]));
        Assert.Throws<InvalidOperationException>(() => Settlement.Of(Shop, Account, Monday, [Cod(100, Tuesday)]));
        Assert.Throws<InvalidOperationException>(() => Settlement.Of(Shop + 1, Account, Monday, [Cod(100, Monday)]));
    }

    [Fact]
    public void A_payout_is_paid_once()
    {
        var settlement = Settlement.Of(Shop, Account, Monday, [Cod(100, Monday)])!;

        settlement.MarkPaid("REF-1", Now);
        settlement.MarkPaid("REF-2", Now.AddHours(1));

        Assert.Equal((SettlementStatus.Paid, "REF-1", Now), (settlement.Status, settlement.GatewayReference, settlement.PaidOn));
    }

    [Fact]
    public void A_rider_hands_the_cash_in_once_every_stop_is_done_and_a_shortfall_is_recorded()
    {
        var trip = NewTrip();
        trip.Start(Now);

        var stillOut = trip.HandInCash(710, 710, Now);
        trip.Finish();
        var negative = trip.HandInCash(710, -1, Now);
        var handedIn = trip.HandInCash(710, 500, Now);
        var again = trip.HandInCash(710, 710, Now);

        Assert.Equal("trip.cash.notBack", stillOut.Error!.Code);
        Assert.Equal("trip.cash.amount", negative.Error!.Code);
        Assert.True(handedIn.IsSuccess);
        Assert.Equal("trip.cash.done", again.Error!.Code);
        Assert.Equal((710m, 500m, 210m, Now), (trip.CashExpected, trip.CashReceived, trip.CashShort, trip.CashReceivedOn));
    }

    private static LedgerEntry Cod(decimal amount, DateOnly day)
    {
        return LedgerEntry.Cod(Delivered(amount), Paid(amount), day);
    }

    private static Payment Paid(decimal cod)
    {
        return Payment.AtTheDoor(new DoorPayment(1, 1, 1, 1, PaymentMethod.Cash, 60, cod), Now);
    }

    private static Order NewOrder(decimal cod)
    {
        return Order.Create(new NewOrder(
            MerchantId: Shop,
            CustomerId: 1,
            AddressId: 1,
            PickupPointId: 1,
            RecipientName: "Rahim",
            CodAmount: cod,
            DeclaredValue: 0,
            Speed: DeliverySpeed.Combine,
            DoNotHold: false,
            Packages: [new NewPackage("Box", 500)])).Value;
    }

    private static Order OutForDelivery(decimal cod = 0)
    {
        var order = NewOrder(cod);
        order.ReceiveAtHub(1, Mirpur, Now);
        order.HandToRider(Mirpur);

        return order;
    }

    private static Order Delivered(decimal cod)
    {
        var order = OutForDelivery(cod);
        order.MoveTo(OrderStatus.Delivered);

        return order;
    }

    private static Order Refused()
    {
        var order = OutForDelivery(900);
        order.MoveTo(OrderStatus.Refused);

        return order;
    }

    private static Order LeftBehind()
    {
        var order = NewOrder(0);
        order.FollowUpIn(DeliveryGroup.FollowUp(new NewDeliveryGroup(1, 1, Mirpur, Now, TimeZoneInfo.Utc, 2)), Now);

        return order;
    }

    private static Trip NewTrip()
    {
        var phone = Customers.PhoneNumber.Parse("01722000001").Value;
        var rider = WithId(new Rider(Mirpur, "Rafiq", phone, new TripLoad(30, 25_000), null), 5);

        return WithId(Trip.Plan(rider, Tuesday), 9);
    }

    /// <summary>Gives an entity the id the database would, so rules that need a saved entity can be tested.</summary>
    private static T WithId<T>(T entity, long id)
        where T : Entity
    {
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(entity, id);

        return entity;
    }
}
