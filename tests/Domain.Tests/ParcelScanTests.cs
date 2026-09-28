using Domain.Grouping;
using Domain.Orders;

namespace Domain.Tests;

/// <summary>Task 3.2: collecting at the shop, scanning in at the hub, and the delivery's shelf.</summary>
public class ParcelScanTests
{
    private const long Mirpur = 1;
    private const long Gulshan = 2;

    private static readonly DateTime Now = new(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc);

    private static Order NewOrder(int packages = 1)
    {
        var spec = new NewOrder(
            MerchantId: 7,
            CustomerId: 1,
            AddressId: 1,
            PickupPointId: 1,
            RecipientName: "Rahim",
            CodAmount: 0,
            DeclaredValue: 0,
            Speed: DeliverySpeed.Combine,
            DoNotHold: false,
            Packages: [.. Enumerable.Range(1, packages).Select(_ => new NewPackage("Box", 500))]);

        return Order.Create(spec).Value;
    }

    private static DeliveryGroup NewGroup()
    {
        return DeliveryGroup.Open(new NewDeliveryGroup(1, 1, Mirpur, Now, TimeZoneInfo.Utc, 2));
    }

    [Fact]
    public void Collecting_moves_the_order_to_picked_up_once()
    {
        var order = NewOrder(packages: 2);

        var first = order.Collect();
        var again = order.Collect();

        Assert.Equal(ScanOutcome.Recorded, first.Value);
        Assert.Equal(ScanOutcome.AlreadyRecorded, again.Value);
        Assert.Equal(OrderStatus.PickedUp, order.Status);
        Assert.Equal([OrderStatus.Created, OrderStatus.PickedUp], order.History.Select(h => h.Status));
    }

    [Fact]
    public void A_parcel_already_at_the_hub_counts_as_collected()
    {
        var order = NewOrder();
        order.ReceiveAtHub(1, Mirpur, Now);

        Assert.Equal(ScanOutcome.AlreadyRecorded, order.Collect().Value);
        Assert.Equal(OrderStatus.AtHub, order.Status);
    }

    [Theory]
    [InlineData(OrderStatus.Cancelled)]
    [InlineData(OrderStatus.OutForDelivery)]
    public void A_cancelled_or_dispatched_order_is_not_collected(OrderStatus status)
    {
        var order = NewOrder();
        if (status == OrderStatus.OutForDelivery)
        {
            order.ReceiveAtHub(1, Mirpur, Now);
        }

        order.MoveTo(status);

        Assert.Equal("order.scan.collect", order.Collect().Error!.Code);
        Assert.Equal(status, order.Status);
    }

    [Fact]
    public void An_order_is_at_the_hub_once_every_package_is_scanned_in()
    {
        var order = NewOrder(packages: 2);
        order.Collect();

        order.ReceiveAtHub(2, Mirpur, Now);
        var halfway = order.Status;
        order.ReceiveAtHub(1, Mirpur, Now.AddMinutes(1));

        Assert.Equal(OrderStatus.PickedUp, halfway);
        Assert.Equal(OrderStatus.AtHub, order.Status);
        Assert.All(order.Packages, p => Assert.Equal(Mirpur, p.HubId));
        Assert.Equal([Now.AddMinutes(1), Now], order.Packages.Select(p => p.ReceivedOn!.Value));
    }

    [Fact]
    public void A_parcel_that_reaches_the_hub_without_a_pickup_scan_was_collected()
    {
        var order = NewOrder();

        Assert.Equal(ScanOutcome.Recorded, order.ReceiveAtHub(1, Mirpur, Now).Value);

        Assert.Equal(
            [OrderStatus.Created, OrderStatus.PickedUp, OrderStatus.AtHub],
            order.History.Select(h => h.Status));
    }

    [Fact]
    public void Scanning_a_parcel_in_twice_at_the_same_hub_changes_nothing()
    {
        var order = NewOrder();
        order.ReceiveAtHub(1, Mirpur, Now);

        var again = order.ReceiveAtHub(1, Mirpur, Now.AddHours(1));

        Assert.Equal(ScanOutcome.AlreadyRecorded, again.Value);
        Assert.Equal(Now, order.Packages[0].ReceivedOn);
        Assert.Equal(3, order.History.Count);
    }

    [Fact]
    public void A_parcel_moved_on_to_another_hub_is_recorded_there()
    {
        var order = NewOrder();
        order.ReceiveAtHub(1, Gulshan, Now);

        var moved = order.ReceiveAtHub(1, Mirpur, Now.AddHours(5));

        Assert.Equal(ScanOutcome.Recorded, moved.Value);
        Assert.Equal((Mirpur, Now.AddHours(5)), (order.Packages[0].HubId!.Value, order.Packages[0].ReceivedOn!.Value));
        Assert.Equal(OrderStatus.AtHub, order.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void A_package_the_order_does_not_have_is_not_found(int sequence)
    {
        var order = NewOrder(packages: 2);

        Assert.Equal("order.scan.package", order.ReceiveAtHub(sequence, Mirpur, Now).Error!.Code);
        Assert.Equal(OrderStatus.Created, order.Status);
    }

    [Fact]
    public void A_cancelled_order_is_not_received()
    {
        var order = NewOrder();
        order.MoveTo(OrderStatus.Cancelled);

        Assert.Equal("order.scan.receive", order.ReceiveAtHub(1, Mirpur, Now).Error!.Code);
        Assert.Null(order.Packages[0].HubId);
    }

    [Fact]
    public void A_waiting_group_takes_a_shelf_and_keeps_it()
    {
        var group = NewGroup();

        group.PutOnShelf(3);

        Assert.Equal(3, group.Shelf);
        Assert.False(group.NeedsShelf);
        Assert.Throws<InvalidOperationException>(() => group.PutOnShelf(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => NewGroup().PutOnShelf(0));
    }

    [Fact]
    public void A_group_frees_its_shelf_when_it_leaves_the_hub_and_takes_one_again_for_a_re_attempt()
    {
        var group = NewGroup();
        group.PutOnShelf(1);
        group.LockIfDue(group.LocksAt);

        var lockedShelf = group.Shelf;
        group.MoveTo(DeliveryGroupStatus.Dispatched, group.LocksAt);
        var dispatched = (group.Shelf, group.NeedsShelf);
        group.MoveTo(DeliveryGroupStatus.Locked, group.LocksAt.AddHours(20));

        Assert.Equal(1, lockedShelf);
        Assert.Equal((null, false), dispatched);
        Assert.True(group.NeedsShelf);
    }

    [Fact]
    public void A_cancelled_group_takes_no_shelf()
    {
        var group = NewGroup();
        group.PutOnShelf(2);
        group.MoveTo(DeliveryGroupStatus.Cancelled, Now);

        Assert.Null(group.Shelf);
        Assert.Throws<InvalidOperationException>(() => group.PutOnShelf(2));
    }

    [Fact]
    public void A_parcel_loaded_on_the_shuttle_leaves_the_hub_until_it_is_received_at_the_other_end()
    {
        var order = NewOrder();
        order.ReceiveAtHub(1, Mirpur, Now);

        var loaded = order.LoadForShuttle(1, Mirpur, Gulshan);
        var onTheWay = (order.Packages[0].HubId, order.Packages[0].ShuttleToHubId);
        var again = order.LoadForShuttle(1, Mirpur, Gulshan);
        var arrived = order.ReceiveAtHub(1, Gulshan, Now.AddHours(8));

        Assert.Equal(ScanOutcome.Recorded, loaded.Value);
        Assert.Equal((null, Gulshan), onTheWay);
        Assert.Equal(ScanOutcome.AlreadyRecorded, again.Value);
        Assert.Equal(ScanOutcome.Recorded, arrived.Value);
        var package = order.Packages[0];
        Assert.Equal((Gulshan, null, Now.AddHours(8)), (package.HubId, package.ShuttleToHubId, package.ReceivedOn));
        Assert.Equal(OrderStatus.AtHub, order.Status);
    }

    [Fact]
    public void A_package_on_the_shuttle_still_counts_as_in_when_the_last_package_arrives()
    {
        var order = NewOrder(packages: 2);
        order.ReceiveAtHub(1, Mirpur, Now);
        order.LoadForShuttle(1, Mirpur, Gulshan);

        order.ReceiveAtHub(2, Mirpur, Now.AddMinutes(5));

        Assert.Equal(OrderStatus.AtHub, order.Status);
    }

    [Fact]
    public void Only_a_parcel_scanned_in_here_and_delivered_from_elsewhere_goes_on_the_shuttle()
    {
        var notHere = NewOrder();
        var elsewhere = NewOrder();
        elsewhere.ReceiveAtHub(1, Gulshan, Now);
        var home = NewOrder();
        home.ReceiveAtHub(1, Mirpur, Now);

        Assert.Equal("order.scan.shuttle", notHere.LoadForShuttle(1, Mirpur, Gulshan).Error!.Code);
        Assert.Equal("order.scan.notHere", elsewhere.LoadForShuttle(1, Mirpur, Gulshan).Error!.Code);
        Assert.Equal("order.scan.shuttle.home", home.LoadForShuttle(1, Mirpur, Mirpur).Error!.Code);
        Assert.Equal("order.scan.package", home.LoadForShuttle(2, Mirpur, Gulshan).Error!.Code);
        Assert.Equal(Mirpur, home.Packages[0].HubId);
    }

    [Fact]
    public void A_cancelled_order_does_not_travel_on_the_shuttle()
    {
        var order = NewOrder();
        order.ReceiveAtHub(1, Mirpur, Now);
        order.MoveTo(OrderStatus.Cancelled);

        Assert.Equal("order.scan.shuttle", order.LoadForShuttle(1, Mirpur, Gulshan).Error!.Code);
        Assert.Equal(Mirpur, order.Packages[0].HubId);
    }

    [Theory]
    [InlineData("MIR", 7, "MIR-07")]
    [InlineData("GUL", 12, "GUL-12")]
    [InlineData("MIR", 120, "MIR-120")]
    public void A_shelf_is_labelled_with_its_hub_and_number(string hub, int shelf, string label)
    {
        Assert.Equal(label, DeliveryGroup.ShelfCode(hub, shelf));
    }
}
