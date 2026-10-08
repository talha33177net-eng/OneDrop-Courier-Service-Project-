using Domain.Common;

namespace Domain.Merchants;

/// <summary>
/// What a moderator may do inside a merchant account. Stored as INT; the values are a bit field, so never renumber
/// one that has been saved.
/// </summary>
[Flags]
public enum MerchantPermissions
{
    None = 0,

    /// <summary>The dashboard and the account's own figures.</summary>
    Dashboard = 1,

    /// <summary>See the parcels, their history and their labels.</summary>
    Parcels = 2,

    /// <summary>Book parcels, upload them, correct and cancel them, and ask for pickups.</summary>
    Booking = 4,

    /// <summary>The statement, the payouts and the invoices.</summary>
    Payments = 8,

    /// <summary>The fraud check and the rate calculator.</summary>
    Tools = 16,

    /// <summary>The account's settings: profile, payout account, pickup points, businesses, API keys and webhook.</summary>
    Settings = 32,

    /// <summary>What a new moderator starts with, as the page says when one is added.</summary>
    Default = Dashboard | Parcels | Booking,

    All = Dashboard | Parcels | Booking | Payments | Tools | Settings
}

/// <summary>
/// Someone who works inside a merchant account with a sign-in of their own: a shop's staff booking parcels or
/// checking payments without sharing the owner's password. A moderator belongs to the account (its main profile), so
/// one moderator works in every business of that account, and the owner chooses what they may see and do. The row
/// carries the permissions; the login itself is an ordinary merchant user, created and stopped with this row.
/// </summary>
public class Moderator : TenantEntity, IArchivable
{
    public const int MaxNameLength = 200;

    private Moderator()
    {
    }

    private Moderator(long accountId, long userId, string name, string? phone, MerchantPermissions permissions)
    {
        AccountId = accountId;
        UserId = userId;
        Name = name;
        Phone = phone;
        Permissions = permissions;
    }

    /// <summary>
    /// The account the moderator works in: a merchant's main profile. Not the business being worked in, which the
    /// moderator switches like the owner does, so this is not a merchant-owned row.
    /// </summary>
    public long AccountId { get; private set; }

    /// <summary>The login they sign in with. One login is one moderator.</summary>
    public long UserId { get; private set; }

    public string Name { get; private set; } = "";

    /// <summary>E.164, so the shop can reach them. Null when none was given.</summary>
    public string? Phone { get; private set; }

    public MerchantPermissions Permissions { get; private set; }

    /// <summary>Stopped by the owner: the login is locked out and the row is kept for the record.</summary>
    public bool Archived { get; private set; }

    public static Result<Moderator> Add(long accountId, long userId, string? name, string? phone, MerchantPermissions permissions)
    {
        var trimmed = name.NullIfBlank();
        if (trimmed is null || trimmed.Length > MaxNameLength)
        {
            return Error.Validation("moderator.name", $"Enter their name, at most {MaxNameLength} characters.");
        }

        if (permissions == MerchantPermissions.None)
        {
            return Error.Validation("moderator.permissions", "Choose at least one thing this person may do.");
        }

        return new Moderator(accountId, userId, trimmed, phone, permissions);
    }

    public Result ChangePermissions(MerchantPermissions permissions)
    {
        if (Archived)
        {
            return Error.Validation("moderator.stopped", $"{Name} has been stopped. Let them in again first.");
        }

        if (permissions == MerchantPermissions.None)
        {
            return Error.Validation("moderator.permissions", "Choose at least one thing this person may do, or stop them.");
        }

        Permissions = permissions;

        return Result.Success();
    }

    public bool May(MerchantPermissions permission)
    {
        return !Archived && (permission == MerchantPermissions.None || Permissions.HasFlag(permission));
    }

    public void Stop()
    {
        Archived = true;
    }

    public void LetBackIn()
    {
        Archived = false;
    }
}
