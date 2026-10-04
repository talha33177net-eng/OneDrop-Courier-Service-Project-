using Domain.Common;

namespace Domain.Merchants;

/// <summary>Stored as TINYINT. Never renumber a value that has been saved.</summary>
public enum MerchantStatus : byte
{
    /// <summary>Signed up and waiting for the courier to approve the account; cannot book parcels yet.</summary>
    Pending = 1,

    Active = 2,

    /// <summary>Stopped by the courier: cannot book parcels, sees its parcels and payments.</summary>
    Suspended = 3
}

/// <summary>Where a merchant's payouts are sent. Stored as TINYINT. Never renumber a value that has been saved.</summary>
public enum PayoutMethod : byte
{
    Bkash = 1,
    Nagad = 2,
    Bank = 3
}

/// <summary>The business details a merchant gives when signing up or editing its profile.</summary>
public sealed record MerchantProfile(
    string? Name,
    string? OwnerName,
    string? Phone,
    string? Email,
    string? Address);

/// <summary>
/// A shop that sends parcels with the courier. It books parcels on the panel or by API, has them collected from its
/// pickup points, pays the delivery charges out of the cash collected for it and is paid the rest
/// (<see cref="Payments.Payout"/>).
/// </summary>
public class Merchant : TenantEntity, IArchivable
{
    public const int MaxWebhookUrlLength = 500;

    private Merchant()
    {
    }

    public string Name { get; private set; } = "";

    public string OwnerName { get; private set; } = "";

    /// <summary>E.164.</summary>
    public string ContactPhone { get; private set; } = "";

    public string? ContactEmail { get; private set; }

    /// <summary>The business address, for the invoice and the courier's records.</summary>
    public string Address { get; private set; } = "";

    public MerchantStatus Status { get; private set; }

    public PayoutMethod? PayoutMethod { get; private set; }

    /// <summary>The bKash or Nagad number (E.164), or the bank account number.</summary>
    public string? PayoutAccount { get; private set; }

    /// <summary>The name on the account; for a bank, also the bank and branch.</summary>
    public string? PayoutAccountName { get; private set; }

    /// <summary>Where the shop's parcel status changes are posted. Null: the shop takes no webhooks.</summary>
    public string? WebhookUrl { get; private set; }

    /// <summary>
    /// The key the shop checks our webhook signatures with (<see cref="WebhookSignature"/>). Stored as it is: signing
    /// needs the secret itself, unlike an API key, which is only compared. Kept when the URL is removed.
    /// </summary>
    public string? WebhookSecret { get; private set; }

    public bool Archived { get; private set; }

    /// <summary>Only an active merchant books parcels and asks for pickups.</summary>
    public bool CanBook => Status == MerchantStatus.Active && !Archived;

    public bool HasPayoutAccount => PayoutMethod is not null && PayoutAccount is not null;

    /// <summary>A shop that signed up itself: it waits for the courier to approve it.</summary>
    public static Result<Merchant> SignUp(MerchantProfile profile)
    {
        return New(profile, MerchantStatus.Pending);
    }

    /// <summary>A shop the courier's admin added: it can book at once.</summary>
    public static Result<Merchant> Add(MerchantProfile profile)
    {
        return New(profile, MerchantStatus.Active);
    }

    public Result Edit(MerchantProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Trim().Length > 200)
        {
            return Error.Validation("merchant.name", "Enter the business name, at most 200 characters.");
        }

        if (string.IsNullOrWhiteSpace(profile.OwnerName) || profile.OwnerName.Trim().Length > 200)
        {
            return Error.Validation("merchant.owner", "Enter the owner's name, at most 200 characters.");
        }

        var phone = PhoneNumber.Parse(profile.Phone);
        if (phone.IsFailure)
        {
            return phone.Error!;
        }

        var email = profile.Email.NullIfBlank();
        if (email is not null && (email.Length > 320 || !email.Contains('@')))
        {
            return Error.Validation("merchant.email", "Enter a valid email address.");
        }

        if (string.IsNullOrWhiteSpace(profile.Address) || profile.Address.Trim().Length > 500)
        {
            return Error.Validation("merchant.address", "Enter the business address, at most 500 characters.");
        }

        Name = profile.Name.Trim();
        OwnerName = profile.OwnerName.Trim();
        ContactPhone = phone.Value.Value;
        ContactEmail = email;
        Address = profile.Address.Trim();

        return Result.Success();
    }

    public Result Approve()
    {
        if (Status != MerchantStatus.Pending)
        {
            return Error.Conflict("merchant.approve", $"{Name} is not waiting for approval.");
        }

        Status = MerchantStatus.Active;

        return Result.Success();
    }

    public Result Suspend()
    {
        if (Status == MerchantStatus.Suspended)
        {
            return Error.Conflict("merchant.suspend", $"{Name} is already suspended.");
        }

        Status = MerchantStatus.Suspended;

        return Result.Success();
    }

    public Result Reactivate()
    {
        if (Status != MerchantStatus.Suspended)
        {
            return Error.Conflict("merchant.reactivate", $"{Name} is not suspended.");
        }

        Status = MerchantStatus.Active;

        return Result.Success();
    }

    /// <summary>
    /// Where payouts go: a bKash or Nagad mobile number, or a bank account with the bank and branch in
    /// <paramref name="accountName"/>.
    /// </summary>
    public Result SetPayoutAccount(PayoutMethod method, string? account, string? accountName)
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
                method == Merchants.PayoutMethod.Bank
                    ? "Enter the account name, bank and branch, at most 200 characters."
                    : "Enter the name on the account, at most 200 characters.");
        }

        string number;
        if (method == Merchants.PayoutMethod.Bank)
        {
            var digits = account.NullIfBlank();
            if (digits is null || digits.Length is < 6 or > 30 || !digits.All(c => char.IsAsciiDigit(c) || c is '-' or ' '))
            {
                return Error.Validation("merchant.payout.account", "Enter the bank account number.");
            }

            number = digits;
        }
        else
        {
            var phone = PhoneNumber.Parse(account);
            if (phone.IsFailure)
            {
                return Error.Validation("merchant.payout.account", "Enter the bKash or Nagad number, such as 01712345678.");
            }

            number = phone.Value.Value;
        }

        PayoutMethod = method;
        PayoutAccount = number;
        PayoutAccountName = name;

        return Result.Success();
    }

    /// <summary>
    /// Sets where webhooks go, and gives the shop a secret the first time. The address must be https, so the parcel
    /// data and the signature cross the internet encrypted; plain http only to this machine (localhost), for testing.
    /// </summary>
    public Result SetWebhook(string? url)
    {
        var trimmed = url.NullIfBlank();
        if (trimmed is null || trimmed.Length > MaxWebhookUrlLength ||
            !Uri.TryCreate(trimmed, UriKind.Absolute, out var address) ||
            address.Scheme is not ("https" or "http") || address.UserInfo.Length > 0)
        {
            return Error.Validation(
                "merchant.webhook.url",
                $"Enter the full address of your webhook, starting with https://, at most {MaxWebhookUrlLength} " +
                "characters.");
        }

        if (address.Scheme == "http" && !address.IsLoopback &&
            !address.Host.EndsWith(".localhost", StringComparison.Ordinal))
        {
            return Error.Validation("merchant.webhook.https", "The webhook address must start with https://.");
        }

        WebhookUrl = address.AbsoluteUri;
        WebhookSecret ??= WebhookSignature.NewSecret();

        return Result.Success();
    }

    /// <summary>A new secret, for when the old one may have leaked. Signatures made with the old one stop verifying.</summary>
    public void NewWebhookSecret()
    {
        WebhookSecret = WebhookSignature.NewSecret();
    }

    public void RemoveWebhook()
    {
        WebhookUrl = null;
    }

    private static Result<Merchant> New(MerchantProfile profile, MerchantStatus status)
    {
        var merchant = new Merchant { Status = status };
        var edited = merchant.Edit(profile);

        return edited.IsSuccess ? merchant : edited.Error!;
    }
}
