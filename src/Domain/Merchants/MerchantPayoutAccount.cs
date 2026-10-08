using Domain.Common;

namespace Domain.Merchants;

/// <summary>
/// A bKash, Nagad or bank account a merchant account keeps for its payouts. An account can keep several; the one
/// payouts go to is copied onto the main profile (<see cref="Merchant.SetPayoutAccount"/>), which every business
/// follows. Kept on the account's main profile (<see cref="AccountId"/>) rather than on a business, so it is not
/// merchant-filtered. Removing one archives it.
/// </summary>
public class MerchantPayoutAccount : TenantEntity, IArchivable
{
    private MerchantPayoutAccount()
    {
    }

    /// <summary>The main profile of the merchant account.</summary>
    public long AccountId { get; private set; }

    public PayoutMethod Method { get; private set; }

    /// <summary>The bKash or Nagad number in E.164, or the bank account number.</summary>
    public string Number { get; private set; } = "";

    /// <summary>The name on the account; for a bank, with the bank and branch.</summary>
    public string Name { get; private set; } = "";

    public bool Archived { get; private set; }

    public static Result<MerchantPayoutAccount> Create(long accountId, PayoutMethod method, string? number, string? name)
    {
        var read = PayoutAccounts.Read(method, number, name);
        if (read.IsFailure)
        {
            return read.Error!;
        }

        return new MerchantPayoutAccount { AccountId = accountId, Method = method, Number = read.Value.Number, Name = read.Value.Name };
    }

    /// <summary>Whether this is the account payouts go to now, as the main profile holds it.</summary>
    public bool IsInUse(Merchant main)
    {
        return main.PayoutMethod == Method && main.PayoutAccount == Number;
    }

    public void Archive()
    {
        Archived = true;
    }

    public void Restore()
    {
        Archived = false;
    }
}

public static class PayoutAccounts
{
    /// <summary>
    /// A payout account as it is kept: a bKash or Nagad mobile number in E.164, or a bank account number with the
    /// account name, bank and branch in <paramref name="accountName"/>.
    /// </summary>
    public static Result<(string Number, string Name)> Read(PayoutMethod method, string? account, string? accountName)
    {
        if (!Enum.IsDefined(method))
        {
            return Error.Validation("merchant.payout.method", "Choose bKash, Nagad or a bank account.");
        }

        var name = accountName.NullIfBlank();
        if (name is null || name.Length > 200)
        {
            return Error.Validation(
                "merchant.payout.name",
                method == PayoutMethod.Bank
                    ? "Enter the account name, bank and branch, at most 200 characters."
                    : "Enter the name on the account, at most 200 characters.");
        }

        if (method == PayoutMethod.Bank)
        {
            var digits = account.NullIfBlank();

            return digits is null || digits.Length is < 6 or > 30 || !digits.All(c => char.IsAsciiDigit(c) || c is '-' or ' ')
                ? Error.Validation("merchant.payout.account", "Enter the bank account number.")
                : (digits, name);
        }

        var phone = PhoneNumber.Parse(account);

        return phone.IsFailure
            ? Error.Validation("merchant.payout.account", "Enter the bKash or Nagad number, such as 01712345678.")
            : (phone.Value.Value, name);
    }
}
