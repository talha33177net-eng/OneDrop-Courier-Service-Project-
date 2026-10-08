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
        parcel.ReceiveAt(Build.Mirpur, Today);
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
        returned.ReceiveAt(Build.Mirpur, Today);
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

        // The merchant is told once, however often the gateway's answer is recorded
        Assert.Single(payout.GetDomainEvents().OfType<PayoutPaid>());
    }

    [Fact]
    public void A_refused_transfer_is_kept_with_why_and_stays_waiting()
    {
        var payout = PayoutFor(Delivered(500));

        payout.RecordFailure("  The receiving account was not found.  ", Now);
        payout.RecordFailure(new string('x', 400), Now.AddHours(1));

        Assert.Equal(PayoutStatus.Pending, payout.Status);
        Assert.True(payout.IsStuck);
        Assert.Equal(2, payout.FailedAttempts);
        Assert.Equal(Payout.MaxErrorLength, payout.LastError!.Length);
        Assert.Equal(Now.AddHours(1), payout.LastTriedOn);

        payout.MarkPaid("REF-1", Now.AddHours(2));
        Assert.False(payout.IsStuck);
        Assert.Null(payout.LastError);
        Assert.Throws<InvalidOperationException>(() => payout.RecordFailure("Late", Now.AddHours(3)));
    }

    [Fact]
    public void A_cancelled_payout_frees_its_lines_for_the_next_one_and_can_never_be_paid()
    {
        var merchant = Build.Merchant();
        merchant.SetPayoutAccount(PayoutMethod.Bkash, "01711000001", "Owner");
        var lines = Delivered(500);
        var payout = Payout.Of(merchant, Today, lines)!;

        Assert.True(payout.Cancel(lines).IsSuccess);

        Assert.Equal(PayoutStatus.Cancelled, payout.Status);
        Assert.All(lines, l => Assert.False(l.IsPaidOut));
        Assert.True(payout.Cancel(lines).IsFailure);
        Assert.Throws<InvalidOperationException>(() => payout.MarkPaid("REF-1", Now));

        merchant.SetPayoutAccount(PayoutMethod.Nagad, "01811000002", "Owner");
        var next = Payout.Of(merchant, Today, lines)!;
        Assert.Equal("+8801811000002", next.Account);
    }

    [Fact]
    public void A_paid_payout_cannot_be_cancelled_and_a_payout_frees_only_its_own_lines()
    {
        var payout = PayoutFor(Delivered(500));
        var other = Delivered(700, id: 101);
        Assert.Throws<InvalidOperationException>(() => payout.Cancel(other));

        payout.MarkPaid("REF-1", Now);
        Assert.Equal("payout.cancel", payout.Cancel([]).Error!.Code);
    }

    [Fact]
    public void Held_payouts_wait_and_every_business_of_the_account_follows_the_hold()
    {
        var main = Build.Merchant();
        main.SetPayoutAccount(PayoutMethod.Bkash, "01711000001", "Owner");

        Assert.True(main.HoldPayouts(null).IsFailure);
        Assert.True(main.HoldPayouts(new string('x', 201)).IsFailure);
        Assert.True(main.HoldPayouts("  Checking a damage claim ").IsSuccess);
        Assert.Equal("Checking a damage claim", main.PayoutHold);
        var lines = Delivered(500);
        Assert.Null(Payout.Of(main, Today, lines));
        Assert.All(lines, l => Assert.False(l.IsPaidOut));

        var business = Build.Merchant(id: 9);
        typeof(Merchant).GetProperty(nameof(Merchant.MainMerchantId))!.SetValue(business, main.Id);
        business.FollowAccount(main);
        Assert.True(business.ArePayoutsHeld);

        Assert.True(main.ReleasePayouts().IsSuccess);
        Assert.True(main.ReleasePayouts().IsFailure);
        Assert.NotNull(Payout.Of(main, Today, lines));
    }

    [Fact]
    public void An_adjustment_says_why_counts_either_way_and_settles_what_a_merchant_owes()
    {
        Assert.Equal("ledger.amount", LedgerEntry.Adjust(7, 0, "Why", Today).Error!.Code);
        Assert.Equal("ledger.amount", LedgerEntry.Adjust(7, 10.005m, "Why", Today).Error!.Code);
        Assert.Equal("ledger.amount", LedgerEntry.Adjust(7, LedgerEntry.MaxAdjustment + 1, "Why", Today).Error!.Code);
        Assert.Equal("ledger.note", LedgerEntry.Adjust(7, 100, "  ", Today).Error!.Code);
        Assert.Equal("ledger.note", LedgerEntry.Adjust(7, 100, new string('x', 201), Today).Error!.Code);

        var charge = LedgerEntry.Adjust(7, -50, "Packing tape", Today).Value;
        Assert.Equal((LedgerEntryKind.Adjustment, -50m, "Packing tape", (long?)null), (charge.Kind, charge.Amount, charge.Note, charge.ParcelId));

        // A merchant owing ৳180 in return charges pays it in cash: the credit brings it to nothing, so nothing is paid
        var merchant = Build.Merchant();
        merchant.SetPayoutAccount(PayoutMethod.Bkash, "01711000001", "Owner");
        var returned = Build.Parcel();
        returned.ReceiveAt(Build.Mirpur, Today);
        returned.RequestReturn(null);
        returned.ReturnToMerchant(Build.Mirpur, Now);
        var owes = LedgerEntry.For(returned, Today).ToList();
        var paid = LedgerEntry.Adjust(7, -owes.Sum(l => l.Amount), "Paid at the hub", Today).Value;
        Assert.Null(Payout.Of(merchant, Today, [.. owes, paid]));

        // Its next cash is paid in full, the settled lines going with it at nothing
        var payout = Payout.Of(merchant, Today, [.. owes, paid, .. Delivered(500)])!;
        Assert.Equal(500 - 60 - 5, payout.Amount);
        Assert.Equal((500m, -owes.Sum(l => l.Amount) + 60 + 5, paid.Amount), (payout.CodTotal, payout.ChargesTotal, payout.AdjustmentsTotal));
    }

    private static Payout PayoutFor(IReadOnlyList<LedgerEntry> lines)
    {
        var merchant = Build.Merchant();
        merchant.SetPayoutAccount(PayoutMethod.Bkash, "01711000001", "Owner");

        return Payout.Of(merchant, Today, lines)!;
    }

    private static IReadOnlyList<LedgerEntry> Delivered(decimal cod, long id = 100)
    {
        var parcel = Build.Parcel(cod: cod, id: id);
        parcel.ReceiveAt(Build.Mirpur, Today);
        parcel.AssignTo(5, Build.Mirpur);
        parcel.Deliver(cod, null, Now);

        return LedgerEntry.For(parcel, Today);
    }
}
