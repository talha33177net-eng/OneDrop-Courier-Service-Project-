using Domain.Common;
using Domain.Customers;
using Domain.Grouping;
using Domain.Orders;
using Domain.Payments;

namespace Domain.Tests;

public class CombineDeliveriesTests
{
    private const long Mirpur = 1;

    private static readonly DateTime Now = new(2026, 9, 28, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_spelling_stands_for_another_address_of_the_same_customer_in_the_same_area()
    {
        var home = WithId(new CustomerAddress(1, 10, "House 8, Road 3", "Flat 2A", null), 100);
        var spelling = WithId(new CustomerAddress(1, 10, "Flat 2A, H 8, R 3", null, null), 101);

        spelling.SameAs(home);

        Assert.Equal(100, spelling.SameAsId);
        Assert.Null(home.SameAsId);
    }

    [Fact]
    public void Another_area_another_customer_itself_or_a_spelling_is_never_the_same_address()
    {
        var home = WithId(new CustomerAddress(1, 10, "House 8, Road 3", null, null), 100);
        var office = WithId(new CustomerAddress(1, 11, "House 8, Road 3", null, null), 101);
        var someoneElse = WithId(new CustomerAddress(2, 10, "House 8 Road 3", null, null), 102);
        var spelling = WithId(new CustomerAddress(1, 10, "H 8 R 3", null, null), 103);
        var another = WithId(new CustomerAddress(1, 10, "8/3", null, null), 104);
        spelling.SameAs(home);

        Assert.Throws<InvalidOperationException>(() => office.SameAs(home));
        Assert.Throws<InvalidOperationException>(() => someoneElse.SameAs(home));
        Assert.Throws<InvalidOperationException>(() => home.SameAs(home));
        Assert.Throws<InvalidOperationException>(() => another.SameAs(spelling));
    }

    [Fact]
    public void Keeping_an_address_apart_keeps_the_first_answer()
    {
        var address = new CustomerAddress(1, 10, "House 8, Road 3", null, null);

        address.KeepApart(Now);
        address.KeepApart(Now.AddDays(1));

        Assert.Equal(Now, address.KeptApartOn);
    }

    [Fact]
    public void The_delivery_leaving_sooner_goes_on_and_the_other_is_cancelled()
    {
        var waiting = WithId(DeliveryGroup.Open(Spec(addressId: 100)), 30);
        var fast = WithId(DeliveryGroup.OpenAlone(Spec(addressId: 101)), 31);

        var combined = DeliveryGroup.Combine(waiting, fast).Value;

        Assert.Same(fast, combined.Keeps);
        Assert.Same(waiting, combined.Ends);
        Assert.Equal(DeliveryGroupStatus.Cancelled, waiting.Status);
        Assert.Equal(DeliveryGroupStatus.Locked, fast.Status);
        Assert.Null(combined.Shelf);
    }

    [Fact]
    public void Deliveries_leaving_the_same_day_keep_the_older_one()
    {
        var first = WithId(DeliveryGroup.Open(Spec(addressId: 100)), 30);
        var second = WithId(DeliveryGroup.Open(Spec(addressId: 101)), 31);

        var combined = DeliveryGroup.Combine(second, first).Value;

        Assert.Same(first, combined.Keeps);
        Assert.Equal(DeliveryGroupStatus.Cancelled, second.Status);
    }

    [Fact]
    public void The_cancelled_deliverys_shelf_is_handed_to_the_one_that_goes_on()
    {
        var shelved = WithId(DeliveryGroup.Open(Spec(addressId: 100, openedOn: Now.AddDays(-1))), 30);
        shelved.PutOnShelf(4);
        var newer = WithId(DeliveryGroup.Open(Spec(addressId: 101)), 31);

        var combined = DeliveryGroup.Combine(newer, shelved).Value;

        Assert.Same(shelved, combined.Keeps);
        Assert.Null(combined.Shelf);
        Assert.Equal(4, shelved.Shelf);

        var other = WithId(DeliveryGroup.Open(Spec(addressId: 102, openedOn: Now.AddDays(-1))), 32);
        var onShelf = WithId(DeliveryGroup.Open(Spec(addressId: 103)), 33);
        onShelf.PutOnShelf(7);

        var handed = DeliveryGroup.Combine(other, onShelf).Value;

        Assert.Same(other, handed.Keeps);
        Assert.Equal(7, handed.Shelf);
        Assert.Null(onShelf.Shelf);
        Assert.True(other.NeedsShelf);
    }

    [Fact]
    public void Deliveries_on_shelves_of_their_own_another_customers_or_one_on_its_way_are_not_combined()
    {
        var first = WithId(DeliveryGroup.Open(Spec(addressId: 100)), 30);
        var second = WithId(DeliveryGroup.OpenAlone(Spec(addressId: 101)), 31);
        first.PutOnShelf(1);
        second.PutOnShelf(2);
        var someoneElse = WithId(DeliveryGroup.Open(Spec(addressId: 102, customerId: 2)), 32);
        var otherHub = WithId(DeliveryGroup.Open(Spec(addressId: 103, hubId: 2)), 33);
        var out1 = WithId(DeliveryGroup.OpenAlone(Spec(addressId: 104)), 34);
        out1.MoveTo(DeliveryGroupStatus.Dispatched, Now);
        var fresh = WithId(DeliveryGroup.Open(Spec(addressId: 105)), 35);

        Assert.Equal(DeliveryGroup.NotCombinable, DeliveryGroup.Combine(first, second).Error);
        Assert.Equal(DeliveryGroup.NotCombinable, DeliveryGroup.Combine(fresh, someoneElse).Error);
        Assert.Equal(DeliveryGroup.NotCombinable, DeliveryGroup.Combine(fresh, otherHub).Error);
        Assert.Equal(DeliveryGroup.NotCombinable, DeliveryGroup.Combine(fresh, out1).Error);
        Assert.Equal(DeliveryGroup.NotCombinable, DeliveryGroup.Combine(fresh, fresh).Error);
        Assert.Equal(DeliveryGroupStatus.Open, fresh.Status);
        Assert.Equal(1, first.Shelf);
    }

    [Fact]
    public void A_combined_order_travels_to_the_kept_address_and_keeps_the_merchants_fee()
    {
        var order = NewOrder(addressId: 101);
        order.PlaceIn(WithId(DeliveryGroup.Open(Spec(addressId: 101)), 31), 60);
        var kept = WithId(DeliveryGroup.Open(Spec(addressId: 100)), 30);

        order.CombineInto(kept);

        Assert.Equal(30, order.DeliveryGroupId);
        Assert.Equal(100, order.AddressId);
        Assert.Equal(60, order.AddedFee);
        Assert.Equal("Delivery address confirmed by the customer", order.History[^1].Note);
        Assert.DoesNotContain("DG-", order.History[^1].Note!);
    }

    [Fact]
    public void Only_a_waiting_order_of_the_same_customer_is_combined()
    {
        var delivered = NewOrder(addressId: 101);
        delivered.ReceiveAtHub(1, Mirpur, Now);
        delivered.HandToRider(Mirpur);
        var someoneElse = WithId(DeliveryGroup.Open(Spec(addressId: 102, customerId: 2)), 32);

        Assert.Throws<InvalidOperationException>(() =>
            delivered.CombineInto(WithId(DeliveryGroup.Open(Spec(addressId: 100)), 30)));
        Assert.Throws<InvalidOperationException>(() => NewOrder(addressId: 101).CombineInto(someoneElse));
    }

    [Fact]
    public void Asking_the_customer_gives_the_order_one_link()
    {
        var order = NewOrder(addressId: 101);
        Assert.Null(order.CustomerToken);

        order.AskCustomer();
        var token = order.CustomerToken;
        order.AskCustomer();

        Assert.NotNull(token);
        Assert.Equal(token, order.CustomerToken);
        Assert.False(order.WaitsForCustomer);
    }

    [Fact]
    public void Only_an_advance_moves_to_the_combined_delivery()
    {
        var advance = Payment.InAdvance(1, 31, PaymentMethod.Bkash, 60);
        var door = Payment.AtTheDoor(new DoorPayment(1, 9, 5, 31, PaymentMethod.Cash, 60, 0), Now);

        advance.CoverInstead(30);

        Assert.Equal(30, advance.DeliveryGroupId);
        Assert.Throws<InvalidOperationException>(() => door.CoverInstead(30));
    }

    private static NewDeliveryGroup Spec(long addressId, long customerId = 1, long hubId = Mirpur, DateTime? openedOn = null)
    {
        return new NewDeliveryGroup(customerId, addressId, hubId, openedOn ?? Now, TimeZoneInfo.Utc, 2);
    }

    private static Order NewOrder(long addressId)
    {
        return Order.Create(new NewOrder(
            MerchantId: 7,
            CustomerId: 1,
            AddressId: addressId,
            PickupPointId: 1,
            RecipientName: "Rahim",
            CodAmount: 0,
            DeclaredValue: 0,
            Speed: DeliverySpeed.Combine,
            DoNotHold: false,
            Packages: [new NewPackage("Box", 500)])).Value;
    }

    /// <summary>Gives an entity the id the database would, so rules that need a saved entity can be tested.</summary>
    private static T WithId<T>(T entity, long id)
        where T : Entity
    {
        typeof(Entity).GetProperty(nameof(Entity.Id))!.SetValue(entity, id);

        return entity;
    }
}
