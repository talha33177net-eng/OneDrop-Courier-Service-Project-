using Domain.Merchants;

namespace Domain.Tests;

/// <summary>The payout accounts a merchant account keeps, and the one its payouts go to.</summary>
public class PayoutAccountTests
{
    [Fact]
    public void A_saved_account_is_read_like_the_account_payouts_go_to()
    {
        var bkash = MerchantPayoutAccount.Create(7, PayoutMethod.Bkash, "01711000009", "Nusrat Jahan").Value;
        var bank = MerchantPayoutAccount.Create(7, PayoutMethod.Bank, "1234-5678-90", "Nusrat Jahan, City Bank, Mirpur").Value;

        Assert.Equal(("+8801711000009", "Nusrat Jahan"), (bkash.Number, bkash.Name));
        Assert.Equal("1234-5678-90", bank.Number);
        Assert.Equal("merchant.payout.account", MerchantPayoutAccount.Create(7, PayoutMethod.Nagad, "12345", "Nusrat").Error?.Code);
        Assert.Equal("merchant.payout.account", MerchantPayoutAccount.Create(7, PayoutMethod.Bank, "12AB34", "Nusrat").Error?.Code);
        Assert.Equal("merchant.payout.name", MerchantPayoutAccount.Create(7, PayoutMethod.Bkash, "01711000009", " ").Error?.Code);
        Assert.Equal("merchant.payout.method", MerchantPayoutAccount.Create(7, (PayoutMethod)9, "01711000009", "Nusrat").Error?.Code);
    }

    [Fact]
    public void The_account_in_use_is_the_one_the_main_profile_pays_to()
    {
        var main = Build.Merchant();
        var bkash = MerchantPayoutAccount.Create(7, PayoutMethod.Bkash, "01711000009", "Nusrat Jahan").Value;
        var nagad = MerchantPayoutAccount.Create(7, PayoutMethod.Nagad, "01711000009", "Nusrat Jahan").Value;

        Assert.True(main.SetPayoutAccount(PayoutMethod.Bkash, "01711000009", "Nusrat Jahan").IsSuccess);

        Assert.True(bkash.IsInUse(main));
        Assert.False(nagad.IsInUse(main));
    }
}
