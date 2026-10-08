using Domain.Parcels;

namespace Domain.Tests;

/// <summary>A merchant asking to cancel a parcel on its way or to change its cash, and the courier answering.</summary>
public class ParcelRequestTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc);

    private static Parcel AtHub()
    {
        return Build.Parcel(cod: 1250).Let(p => p.PickUp(Build.Today)).Let(p => p.ReceiveAt(Build.Mirpur, Build.Today));
    }

    [Fact]
    public void An_approved_change_of_cash_is_what_the_rider_collects_and_the_cod_charge_follows()
    {
        var parcel = AtHub();
        var request = ParcelRequest.Ask(parcel, ParcelRequestKind.ChangeCod, 900, "Customer asked for a discount", 42).Value;

        Assert.Equal((ParcelRequestStatus.Open, 1250m, 900m), (request.Status, request.CodAmount, request.NewCodAmount!.Value));
        Assert.True(request.Approve(parcel, null, 7, Now).IsSuccess);

        Assert.Equal((ParcelRequestStatus.Approved, Now, 7L), (request.Status, request.AnsweredOn!.Value, request.AnsweredById!.Value));
        Assert.Equal(900, parcel.CodAmount);
        Assert.Equal("Cash on delivery changed from ৳1,250 to ৳900: the merchant asked: Customer asked for a discount", parcel.Events[^1].Note);
        parcel.AssignTo(5, Build.Mirpur);
        Assert.Equal("parcel.deliver.amount", parcel.Deliver(1250, null, Now).Error?.Code);
        Assert.True(parcel.Deliver(900, null, Now).IsSuccess);
        Assert.Equal(9, parcel.CodCharge);
    }

    [Fact]
    public void An_approved_cancellation_sends_the_parcel_back_and_it_is_charged_as_a_return()
    {
        var parcel = AtHub();
        var request = ParcelRequest.Ask(parcel, ParcelRequestKind.Cancel, null, "Customer cancelled the order", 42).Value;

        Assert.True(request.Approve(parcel, "Done", 7, Now).IsSuccess);

        Assert.Equal((ParcelStatus.Returning, "Cancelled by the merchant: Customer cancelled the order"), (parcel.Status, parcel.ReturnReason));
        Assert.Equal((ParcelRequestStatus.Approved, "Done"), (request.Status, request.Answer));
    }

    [Fact]
    public void Only_a_parcel_on_its_way_is_asked_about_and_a_change_needs_another_amount()
    {
        var parcel = AtHub();

        Assert.Equal("request.parcel", ParcelRequest.Ask(Build.Parcel(), ParcelRequestKind.Cancel, null, "Not needed", 42).Error?.Code);
        Assert.Equal("request.parcel", ParcelRequest.Ask(Build.OutForDelivery().Let(p => p.Deliver(1250, null, Now)), ParcelRequestKind.Cancel, null, "Late", 42).Error?.Code);
        Assert.Equal("request.reason", ParcelRequest.Ask(parcel, ParcelRequestKind.Cancel, null, " ", 42).Error?.Code);
        Assert.Equal("request.cod", ParcelRequest.Ask(parcel, ParcelRequestKind.ChangeCod, 1250, "Same", 42).Error?.Code);
        Assert.Equal("request.cod", ParcelRequest.Ask(parcel, ParcelRequestKind.ChangeCod, -1, "Less", 42).Error?.Code);
        Assert.Equal("request.cod", ParcelRequest.Ask(parcel, ParcelRequestKind.ChangeCod, null, "Less", 42).Error?.Code);
        Assert.Equal("request.kind", ParcelRequest.Ask(parcel, (ParcelRequestKind)9, null, "Odd", 42).Error?.Code);
    }

    [Fact]
    public void A_refusal_says_why_and_an_answered_request_is_not_answered_again()
    {
        var parcel = AtHub();
        var request = ParcelRequest.Ask(parcel, ParcelRequestKind.ChangeCod, 1000, "Discount", 42).Value;

        Assert.Equal("request.answer", request.Refuse(" ", 7, Now).Error?.Code);
        Assert.True(request.Refuse("The customer already paid in full", 7, Now).IsSuccess);

        Assert.Equal(ParcelRequestStatus.Refused, request.Status);
        Assert.Equal(1250, parcel.CodAmount);
        Assert.Equal("request.answered", request.Approve(parcel, null, 7, Now).Error?.Code);
    }

    [Fact]
    public void A_cancellation_cannot_be_approved_while_a_rider_has_the_parcel_and_stays_open()
    {
        var parcel = Build.OutForDelivery();
        var request = ParcelRequest.Ask(parcel, ParcelRequestKind.Cancel, null, "Customer cancelled", 42).Value;

        Assert.Equal("parcel.return", request.Approve(parcel, null, 7, Now).Error?.Code);

        Assert.Equal((ParcelRequestStatus.Open, ParcelStatus.OutForDelivery), (request.Status, parcel.Status));
    }
}
