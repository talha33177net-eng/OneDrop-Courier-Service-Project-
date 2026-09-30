using Domain.Customers;
using Domain.Grouping;
using Domain.Orders;
using Domain.Payments;
using Domain.Pricing;

namespace Domain.Tests;

/// <summary>Task 3.6b: what a new order waits for from the customer, and the fee paid in advance.</summary>
public class AdvancePaymentTests
{
    // The launch tenants' trust settings (DbUp 006 and 008)
    private static readonly TrustRules Rules = new(TrustedAfterDeliveries: 10, TrustedAgainAfterDeliveries: 3);

    // The launch tenant's prices (seed 001, fast fee 003, weight 004)
    private static readonly FeeSchedule Dhaka = new(60, 25, 70, 2000, 15);
    private static readonly DateTime Now = new(2026, 9, 29, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_customers_first_cash_order_is_confirmed_with_one_tap()
    {
        Assert.Equal(CustomerStep.Confirm, Step(CustomerStanding.New, cod: 1200));

        // Once they have taken a delivery, nothing is asked until something goes wrong
        Assert.Equal(CustomerStep.None, Step(CustomerStanding.Clean(1), cod: 1200));
    }

    [Fact]
    public void A_product_paid_online_never_waits()
    {
        Assert.Equal(CustomerStep.None, Step(CustomerStanding.New, cod: 0));
        Assert.Equal(CustomerStep.None, Step(new CustomerStanding(0, 3, 0), cod: 0));
        Assert.Equal(CustomerStep.None, Step(CustomerStanding.New, cod: 0, shopAsks: true));
    }

    [Fact]
    public void A_failed_visit_or_the_shops_request_asks_for_the_fee_in_advance()
    {
        Assert.Equal(CustomerStep.PayInAdvance, Step(new CustomerStanding(0, 1, 0), cod: 1200));
        Assert.Equal(CustomerStep.PayInAdvance, Step(new CustomerStanding(2, 1, 0), cod: 1200));
        Assert.Equal(CustomerStep.PayInAdvance, Step(CustomerStanding.Clean(2), cod: 1200, shopAsks: true));

        // A first order the shop asks about is paid in advance, not merely confirmed
        Assert.Equal(CustomerStep.PayInAdvance, Step(CustomerStanding.New, cod: 1200, shopAsks: true));
    }

    [Fact]
    public void After_a_failure_the_fee_is_paid_in_advance_until_enough_deliveries_are_accepted_since()
    {
        // Task 3.8: 3 deliveries accepted since the last refusal or no-show, whatever came before it
        Assert.Equal(CustomerStep.PayInAdvance, Step(new CustomerStanding(5, 2, 2), cod: 1200));
        Assert.Equal(CustomerStep.None, Step(new CustomerStanding(6, 2, 3), cod: 1200));

        // Back to normal means the shop's own request is heard again
        Assert.Equal(CustomerStep.PayInAdvance, Step(new CustomerStanding(6, 2, 3), cod: 1200, shopAsks: true));
    }

    [Fact]
    public void A_customer_is_trusted_after_enough_deliveries_with_no_failure_since()
    {
        var trusted = CustomerStanding.Clean(10);

        Assert.Equal(CustomerStep.None, Step(trusted, cod: 1200));
        Assert.Equal(CustomerStep.None, Step(trusted, cod: 1200, shopAsks: true));
        Assert.Equal(CustomerStep.None, Step(new CustomerStanding(14, 1, 10), cod: 1200, shopAsks: true));

        // Ten deliveries do not excuse a refusal after them
        Assert.Equal(CustomerStep.PayInAdvance, Step(new CustomerStanding(10, 1, 0), cod: 1200));
        Assert.Equal(CustomerStep.PayInAdvance, Step(CustomerStanding.Clean(9), cod: 1200, shopAsks: true));
    }

    [Fact]
    public void An_order_that_waits_gets_a_token_and_an_order_that_does_not_gets_none()
    {
        var waiting = NewOrder(CustomerStep.Confirm);
        var straight = NewOrder(CustomerStep.None);

        Assert.NotNull(waiting.CustomerToken);
        Assert.NotEqual(waiting.CustomerToken, NewOrder(CustomerStep.Confirm).CustomerToken);
        Assert.True(waiting.WaitsForCustomer);
        Assert.False(waiting.WaitsForAdvance);
        Assert.Null(straight.CustomerToken);
        Assert.False(straight.WaitsForCustomer);
        Assert.Null(straight.ConfirmedOn);
    }

    [Fact]
    public void Confirming_is_recorded_once_and_lets_the_order_be_collected()
    {
        var order = NewOrder(CustomerStep.Confirm);

        // An order only waiting to be confirmed is still collected: the shop is warned, not stopped
        Assert.Equal(ScanOutcome.Recorded, NewOrder(CustomerStep.Confirm).Collect().Value);
        Assert.True(order.Confirm(Now).IsSuccess);
        Assert.True(order.Confirm(Now.AddHours(1)).IsSuccess);
        Assert.Equal(Now, order.ConfirmedOn);
        Assert.False(order.WaitsForCustomer);

        // An order needing nothing is "confirmed" without recording anything
        var straight = NewOrder(CustomerStep.None);
        Assert.True(straight.Confirm(Now).IsSuccess);
        Assert.Null(straight.ConfirmedOn);
    }

    [Fact]
    public void An_order_waiting_for_the_advance_stays_at_the_shop_until_it_is_paid()
    {
        var order = NewOrder(CustomerStep.PayInAdvance);

        Assert.True(order.WaitsForAdvance);
        Assert.Equal("order.scan.advance", order.Collect().Error!.Code);
        Assert.Equal("order.confirm.advance", order.Confirm(Now).Error!.Code);
        Assert.Equal(OrderStatus.Created, order.Status);

        order.AdvancePaid(Now);

        Assert.Equal(Now, order.ConfirmedOn);
        Assert.False(order.WaitsForAdvance);
        Assert.Equal(ScanOutcome.Recorded, order.Collect().Value);
        Assert.Equal(OrderStatus.PickedUp, order.Status);

        // Paying again keeps the first time
        order.AdvancePaid(Now.AddHours(1));
        Assert.Equal(Now, order.ConfirmedOn);
    }

    [Fact]
    public void The_advance_is_the_deliverys_first_shop_fee_whatever_the_other_shops_add()
    {
        var calculator = new DeliveryFeeCalculator(Dhaka);
        FeeLine[] two = [Line(1), Line(2)];

        Assert.Equal(60, calculator.FirstShopFee(two));
        Assert.Equal(85, calculator.GroupFee(two));
        Assert.Equal(70, calculator.FirstShopFee([Line(1), Fast(2)]));
        Assert.Equal(70, calculator.FirstShopFee(two, DeliveryGroupKind.ShippedNow));
        Assert.Equal(25, calculator.FirstShopFee(two, DeliveryGroupKind.FollowUp));

        // A refused order's speed no longer sets the price
        Assert.Equal(60, calculator.FirstShopFee([Line(1), Fast(2) with { Status = OrderStatus.Refused }]));
    }

    [Fact]
    public void An_advance_is_paid_by_wallet_and_holds_no_cash_on_delivery()
    {
        var advance = Payment.InAdvance(customerId: 1, deliveryGroupId: 2, PaymentMethod.Bkash, 60);

        Assert.Equal((PaymentPurpose.Advance, PaymentStatus.Pending, 60m, 0m), (advance.Purpose, advance.Status, advance.Fee, advance.Cod));
        Assert.Null(advance.TripId);
        Assert.Null(advance.RiderId);

        advance.RequestedAs("BKASH-1", "https://pay.fake/bkash/BKASH-1");
        Assert.True(advance.MarkPaid(Now).IsSuccess);
        Assert.Same(advance, Assert.IsType<PaymentReceived>(Assert.Single(advance.GetDomainEvents())).Payment);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => Payment.InAdvance(customerId: 1, deliveryGroupId: 2, PaymentMethod.Cash, 60));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Payment.InAdvance(customerId: 1, deliveryGroupId: 2, PaymentMethod.Nagad, 0));
    }

    private static CustomerStep Step(CustomerStanding standing, decimal cod, bool shopAsks = false)
    {
        return standing.StepFor(cod, shopAsks, Rules);
    }

    private static FeeLine Line(long merchantId)
    {
        return new FeeLine(merchantId, DeliverySpeed.Combine, OrderStatus.Created, 500);
    }

    private static FeeLine Fast(long merchantId)
    {
        return Line(merchantId) with { Speed = DeliverySpeed.Fast };
    }

    private static Order NewOrder(CustomerStep step)
    {
        return Order.Create(new NewOrder(
            MerchantId: 1,
            CustomerId: 1,
            AddressId: 1,
            PickupPointId: 1,
            RecipientName: "Rahim",
            CodAmount: 1200,
            DeclaredValue: 1200,
            Speed: DeliverySpeed.Combine,
            DoNotHold: false,
            Packages: [new NewPackage("Box", 500)])
        {
            CustomerStep = step
        }).Value;
    }
}
