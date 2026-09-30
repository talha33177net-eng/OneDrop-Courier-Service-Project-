using Domain.Customers;
using Domain.Grouping;
using Domain.Merchants;
using Domain.Orders;

namespace Domain.Tests;

/// <summary>Task 3.8: a shop's late handovers, and when a shop late too often brings its parcels to the hub.</summary>
public class DropOffTests
{
    private const long Mirpur = 3;

    // The launch tenants' rule (DbUp 008): 3 late handovers in 30 days
    private static readonly DropOffRule Rule = new(AfterLateHandovers: 3, WindowDays: 30);
    private static readonly DateTime Now = new(2026, 9, 30, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void An_order_the_shop_had_not_handed_over_is_its_late_handover_the_first_time_only()
    {
        var order = NewOrder();

        Assert.True(order.FollowUpIn(FollowUp(), Now));
        Assert.Equal(Now, order.ShopLateOn);

        // Left behind again: the same late handover, not a second one
        Assert.False(order.FollowUpIn(FollowUp(), Now.AddDays(1)));
        Assert.Equal(Now, order.ShopLateOn);
    }

    [Fact]
    public void An_order_already_with_us_or_held_for_the_customers_advance_is_not_the_shops_fault()
    {
        var collected = NewOrder();
        collected.Collect();
        var atHub = NewOrder();
        atHub.ReceiveAtHub(1, Mirpur, Now);
        var waitingForAdvance = NewOrder(CustomerStep.PayInAdvance);

        Assert.False(collected.FollowUpIn(FollowUp(), Now));
        Assert.False(atHub.FollowUpIn(FollowUp(), Now));
        Assert.False(waitingForAdvance.FollowUpIn(FollowUp(), Now));
        Assert.All([collected, atHub, waitingForAdvance], order => Assert.Null(order.ShopLateOn));
        Assert.All([collected, atHub, waitingForAdvance], order => Assert.Equal(Now, order.LeftBehindOn));
    }

    [Fact]
    public void A_shop_drops_off_at_the_hub_from_its_third_late_handover_within_the_window()
    {
        Assert.Null(Rule.DropsOffUntil([], Now));
        Assert.Null(Rule.DropsOffUntil([Now.AddDays(-1), Now.AddDays(-2)], Now));

        // Until the oldest of the three leaves the window: 30 days after it
        Assert.Equal(
            Now.AddDays(-5).AddDays(30),
            Rule.DropsOffUntil([Now.AddDays(-1), Now.AddDays(-2), Now.AddDays(-5)], Now));
    }

    [Fact]
    public void Late_handovers_older_than_the_window_do_not_count_and_more_of_them_keep_the_shop_longer()
    {
        Assert.Null(Rule.DropsOffUntil([Now.AddDays(-1), Now.AddDays(-2), Now.AddDays(-30)], Now));
        Assert.Null(Rule.DropsOffUntil([Now.AddDays(-1), Now.AddDays(-2), Now.AddDays(-45)], Now));

        // Four late handovers: the shop drops off until the third newest leaves the window
        Assert.Equal(
            Now.AddDays(-3).AddDays(30),
            Rule.DropsOffUntil([Now.AddDays(-20), Now.AddDays(-1), Now.AddDays(-3), Now.AddDays(-2)], Now));

        // The day it ends the shop is back on the route
        var until = Rule.DropsOffUntil([Now.AddDays(-1), Now.AddDays(-2), Now.AddDays(-5)], Now)!.Value;
        Assert.Null(Rule.DropsOffUntil([Now.AddDays(-1), Now.AddDays(-2), Now.AddDays(-5)], until));
    }

    [Fact]
    public void Each_tenant_sets_its_own_count_and_window()
    {
        var strict = new DropOffRule(AfterLateHandovers: 1, WindowDays: 7);

        Assert.Equal(Now.AddDays(-6).AddDays(7), strict.DropsOffUntil([Now.AddDays(-6)], Now));
        Assert.Null(strict.DropsOffUntil([Now.AddDays(-8)], Now));
        Assert.Null(Rule.DropsOffUntil([Now.AddDays(-6)], Now));
    }

    private static DeliveryGroup FollowUp()
    {
        return DeliveryGroup.FollowUp(new NewDeliveryGroup(1, 1, Mirpur, Now, TimeZoneInfo.Utc, 2));
    }

    private static Order NewOrder(CustomerStep step = CustomerStep.None)
    {
        return Order.Create(new NewOrder(
            MerchantId: 7,
            CustomerId: 1,
            AddressId: 1,
            PickupPointId: 1,
            RecipientName: "Rahim",
            CodAmount: 900,
            DeclaredValue: 900,
            Speed: DeliverySpeed.Combine,
            DoNotHold: false,
            Packages: [new NewPackage("Box", 500)])
        {
            CustomerStep = step
        }).Value;
    }
}
