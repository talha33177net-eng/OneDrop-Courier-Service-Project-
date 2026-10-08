using Domain.Delivery;
using Domain.Parcels;

namespace Domain.Tests;

/// <summary>Returning parcels a hub sends back to their merchant with a rider, and the merchant signing for them.</summary>
public class ReturnListTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void A_hub_sends_returns_back_with_a_rider_who_hands_them_over_and_the_merchant_confirms()
    {
        var first = Build.Returning(id: 100);
        var second = Build.Returning(id: 101);

        var list = ReturnList.Send(Build.Rider(), Build.Mirpur, [first, second]).Value;

        Assert.Equal((ReturnListStatus.Out, 2), (list.Status, list.Parcels.Count));
        Assert.Equal((ParcelStatus.Returning, null, 5L), (first.Status, first.CurrentHubId, first.RiderId));
        Assert.Equal("Out to the merchant with the rider", first.Events[^1].Note);

        Assert.True(list.HandOver([first, second], Now).IsSuccess);

        Assert.Equal((ReturnListStatus.HandedOver, Now), (list.Status, list.HandedOverOn));
        Assert.Equal((ParcelStatus.Returned, null, Now), (first.Status, first.RiderId, first.ClosedOn));
        Assert.Equal(first.DeliveryCharge + first.ReturnCharge, first.TotalCharge);
        Assert.Equal(ParcelStatus.Returned, Assert.IsType<ParcelStatusChanged>(Assert.Single(second.GetDomainEvents())).Status);

        Assert.True(list.Confirm(42, " One box was crushed ", Now).IsSuccess);
        Assert.Equal((ReturnListStatus.Confirmed, Now, 42L, "One box was crushed"), (list.Status, list.ConfirmedOn, list.ConfirmedById, list.Note));
        Assert.Equal("returnList.confirmed", list.Confirm(42, null, Now).Error?.Code);
    }

    [Fact]
    public void A_list_goes_to_one_pickup_point_with_a_rider_of_the_hub_and_sends_none_unless_all_can_go()
    {
        var waiting = Build.Returning(id: 100);
        var atHub = Build.Parcel(id: 101).Let(p => p.PickUp(Build.Today)).Let(p => p.ReceiveAt(Build.Mirpur, Build.Today));
        var flagged = Build.Returning(id: 102).Let(p => p.Flag(ParcelIssue.Exceptional, "Box torn", null, Now));
        var otherPoint = Build.Returning(id: 103, pickupPoint: 2);
        var otherHub = Build.Returning(id: 104, pickupHub: Build.Gulshan);

        Assert.Equal("parcel.sendBack.notHere", ReturnList.Send(Build.Rider(), Build.Mirpur, [waiting, atHub]).Error?.Code);
        Assert.Equal("parcel.sendBack.flagged", ReturnList.Send(Build.Rider(), Build.Mirpur, [waiting, flagged]).Error?.Code);
        Assert.Equal("parcel.sendBack.otherHub", ReturnList.Send(Build.Rider(), Build.Mirpur, [otherHub]).Error?.Code);
        Assert.Equal("returnList.mixed", ReturnList.Send(Build.Rider(), Build.Mirpur, [waiting, otherPoint]).Error?.Code);
        Assert.Equal("returnList.rider", ReturnList.Send(Build.Rider(hub: Build.Gulshan), Build.Mirpur, [waiting]).Error?.Code);
        Assert.Equal("returnList.none", ReturnList.Send(Build.Rider(), Build.Mirpur, []).Error?.Code);

        // Nothing was sent: the parcel that could go is still waiting at the hub
        Assert.Equal((Build.Mirpur, (long?)null), (waiting.CurrentHubId, waiting.RiderId));
    }

    [Fact]
    public void Returns_the_rider_could_not_hand_over_stay_with_them_until_the_hub_scans_them_in_and_can_go_again()
    {
        var parcel = Build.Returning();
        var list = ReturnList.Send(Build.Rider(), Build.Mirpur, [parcel]).Value;

        Assert.Equal("returnList.reason", list.Miss([parcel], " ").Error?.Code);
        Assert.True(list.Miss([parcel], "Shop closed").IsSuccess);

        Assert.Equal((ReturnListStatus.NotHandedOver, "Shop closed"), (list.Status, list.Note));
        Assert.Equal((ParcelStatus.Returning, 5L), (parcel.Status, parcel.RiderId));
        Assert.Equal("Not handed back to the merchant: Shop closed", parcel.Events[^1].Note);
        Assert.Equal("returnList.done", list.HandOver([parcel], Now).Error?.Code);
        Assert.Equal("returnList.notHandedOver", list.Confirm(42, null, Now).Error?.Code);
        Assert.Equal("parcel.sendBack.notHere", ReturnList.Send(Build.Rider(), Build.Mirpur, [parcel]).Error?.Code);

        Assert.Equal(ScanOutcome.Recorded, parcel.ReceiveAt(Build.Mirpur, Build.Today).Value);
        Assert.True(ReturnList.Send(Build.Rider(), Build.Mirpur, [parcel]).IsSuccess);
    }

    [Fact]
    public void Only_the_rider_who_has_the_parcel_hands_it_back()
    {
        var parcel = Build.Returning();
        ReturnList.Send(Build.Rider(), Build.Mirpur, [parcel]);

        Assert.Equal("parcel.handBack.notWithRider", parcel.HandBackAtDoor(6, Now).Error?.Code);
        Assert.Equal("parcel.handBack.notWithRider", parcel.MissHandBack(6, "Closed").Error?.Code);
        Assert.True(parcel.HandBackAtDoor(5, Now).IsSuccess);
        Assert.Equal("parcel.handBack.notWithRider", parcel.HandBackAtDoor(5, Now).Error?.Code);
    }
}
