using Domain.Merchants;
using Domain.Parcels;
using Domain.Payments;

namespace Domain.Tests;

/// <summary>The merchant's ledger lines for a finished parcel, and the payouts that pay them.</summary>
public class MoneyTests
{
    private static readonly DateTime Now = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateOnly Today = new(2026, 10, 4);

    [Fact]
    public void A_delivered_parcel_owes_the_merchant_its_cash_less_the_delivery_and_cod_charges()
    {
        var parcel = Build.OutForDelivery(cod: 1250);
        parcel.Deliver(1250, null, Now);

        var lines = LedgerEntry.For(parcel, Today);

        Assert.Equal(
            [(LedgerEntryKind.Cod, 1250m), (LedgerEntryKind.DeliveryCharge, -60m), (LedgerEntryKind.CodCharge, -13m)],
            lines.Select(l => (l.Kind, l.Amount)));
        Assert.All(lines, l => Assert.Equal(7, l.MerchantId));
        Assert.Equal(1177, lines.Sum(l => l.Amount));
    }

    [Fact]
    public void A_parcel_paid_online_writes_no_line_of_nothing()
    {
        var parcel = Build.OutForDelivery(cod: 0);
        parcel.Deliver(0, null, Now);

        Assert.Equal([LedgerEntryKind.DeliveryCharge], LedgerEntry.For(parcel, Today).Select(l => l.Kind));
    }

    [Fact]
    public void A_returned_parcel_costs_its_delivery_and_return_charges()
    {
        var parcel = Build.Parcel(area: Pricing.ServiceArea.OutsideCity);
        parcel.ReceiveAt(Build.Mirpur);
        parcel.RequestReturn("Merchant wants it back");
        parcel.ReturnToMerchant(Build.Mirpur, Now);

        var lines = LedgerEntry.For(parcel, Today);

        Assert.Equal([(LedgerEntryKind.DeliveryCharge, -120m), (LedgerEntryKind.ReturnCharge, -60m)], lines.Select(l => (l.Kind, l.Amount)));
    }

    [Fact]
    public void Lines_are_written_only_for_a_finished_parcel()
    {
        Assert.Throws<InvalidOperationException>(() => LedgerEntry.For(Build.OutForDelivery(), Today));
    }

    [Fact]
    public void A_payout_takes_every_line_and_pays_the_cash_less_charges_to_the_merchants_account()
    {
        var merchant = Build.Merchant();
        merchant.SetPayoutAccount(PayoutMethod.Bkash, "01711000001", "Nusrat Jahan");
        var lines = Delivered(1250).Concat(Delivered(800, id: 101)).ToList();

        var payout = Payout.Of(merchant, Today, lines)!;

        Assert.Equal(2050, payout.CodTotal);
        Assert.Equal(60 + 13 + 60 + 8, payout.ChargesTotal);
        Assert.Equal(2050 - 141, payout.Amount);
        Assert.Equal("+8801711000001", payout.Account);
        Assert.Equal(PayoutMethod.Bkash, payout.Method);
        Assert.All(lines, l => Assert.True(l.IsPaidOut));
        Assert.Throws<InvalidOperationException>(() => Payout.Of(merchant, Today, lines));
    }

    [Fact]
    public void Charges_more_than_the_cash_or_no_payout_account_wait_for_the_next_payout()
    {
        var merchant = Build.Merchant();
        var returned = Build.Parcel();
        returned.ReceiveAt(Build.Mirpur);
        returned.RequestReturn(null);
        returned.ReturnToMerchant(Build.Mirpur, Now);
        var owes = LedgerEntry.For(returned, Today);

        Assert.Null(Payout.Of(merchant, Today, Delivered(1250)));

        merchant.SetPayoutAccount(PayoutMethod.Nagad, "01711000001", "Nusrat Jahan");
        Assert.Null(Payout.Of(merchant, Today, owes));
        Assert.All(owes, l => Assert.False(l.IsPaidOut));
    }

    [Fact]
    public void A_payout_never_takes_another_merchants_line_or_one_after_its_day()
    {
        var merchant = Build.Merchant(id: 8);
        merchant.SetPayoutAccount(PayoutMethod.Bkash, "01711000001", "Owner");

        Assert.Throws<InvalidOperationException>(() => Payout.Of(merchant, Today, Delivered(1000)));
        Assert.Throws<InvalidOperationException>(() => Payout.Of(Build.Merchant().Let(m => m.SetPayoutAccount(PayoutMethod.Bkash, "01711000001", "Owner")), Today.AddDays(-1), Delivered(1000)));
    }

    [Fact]
    public void A_payout_is_marked_paid_once()
    {
        var merchant = Build.Merchant();
        merchant.SetPayoutAccount(PayoutMethod.Bank, "1501203456789", "Owner, City Bank, Dhanmondi");
        var payout = Payout.Of(merchant, Today, Delivered(500))!;

        payout.MarkPaid("REF-1", Now);
        payout.MarkPaid("REF-2", Now.AddHours(1));

        Assert.Equal(PayoutStatus.Paid, payout.Status);
        Assert.Equal("REF-1", payout.GatewayReference);
        Assert.Equal("1501203456789", payout.Account);
    }

    private static IReadOnlyList<LedgerEntry> Delivered(decimal cod, long id = 100)
    {
        var parcel = Build.Parcel(cod: cod, id: id);
        parcel.ReceiveAt(Build.Mirpur);
        parcel.AssignTo(5, Build.Mirpur);
        parcel.Deliver(cod, null, Now);

        return LedgerEntry.For(parcel, Today);
    }
}
