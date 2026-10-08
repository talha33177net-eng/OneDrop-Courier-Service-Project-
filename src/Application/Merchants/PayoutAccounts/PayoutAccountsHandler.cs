using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Common;
using Domain.Merchants;

namespace Application.Merchants.PayoutAccounts;

/// <summary>A saved payout account; <see cref="InUse"/> when payouts go there now.</summary>
public sealed record PayoutAccountView(long Id, PayoutMethod Method, string Number, string Name, bool InUse);

/// <summary>Where the account's payouts go now (null before it has given one), and the accounts it keeps.</summary>
public sealed record PayoutAccountsView(PayoutMethod? Method, string? Number, string? Name, IReadOnlyList<PayoutAccountView> Saved);

/// <summary>
/// The payout accounts a merchant account keeps: bKash, Nagad and bank accounts, one of them where payouts go. They are
/// the account's, shared by every business; choosing one sets it on the main profile and every business follows. A
/// login reaches only its own account's (they are kept by account, so each query names it).
/// </summary>
public class PayoutAccountsHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public static readonly Error NotFound = Error.NotFound("payoutAccount.notFound", "That payout account was not found.");

    public async Task<PayoutAccountsView> GetAsync(CancellationToken cancellationToken = default)
    {
        var accountId = AccountId();
        var main = (await AccountAsync(accountId, cancellationToken)).Single(m => m.Id == accountId);
        var saved = await db.MerchantPayoutAccounts
            .Where(a => a.AccountId == accountId && !a.Archived)
            .OrderBy(a => a.Id)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new PayoutAccountsView(
            main.PayoutMethod,
            main.PayoutAccount,
            main.PayoutAccountName,
            [.. saved.Select(a => new PayoutAccountView(a.Id, a.Method, a.Number, a.Name, a.IsInUse(main)))]);
    }

    /// <summary>
    /// Keeps a new account, and sends payouts there when <paramref name="use"/> is set or the account has none yet.
    /// Adding a kept one again only chooses it.
    /// </summary>
    public async Task<Result> AddAsync(PayoutMethod method, string? number, string? name, bool use, CancellationToken cancellationToken = default)
    {
        var accountId = AccountId();
        var created = MerchantPayoutAccount.Create(accountId, method, number, name);
        if (created.IsFailure)
        {
            return created.Error!;
        }

        var kept = await db.MerchantPayoutAccounts.SingleOrDefaultAsync(
            a => a.AccountId == accountId && !a.Archived && a.Method == created.Value.Method && a.Number == created.Value.Number,
            cancellationToken);
        if (kept is not null && !use)
        {
            return Error.Conflict("payoutAccount.kept", "That account is saved already.");
        }

        if (kept is null)
        {
            db.MerchantPayoutAccounts.Add(created.Value);
        }

        var businesses = await AccountAsync(accountId, cancellationToken);
        var main = businesses.Single(m => m.Id == accountId);
        if (use || !main.HasPayoutAccount)
        {
            var chosen = Choose(businesses, kept ?? created.Value);
            if (chosen.IsFailure)
            {
                return chosen;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>Payouts go to the kept account <paramref name="id"/> from now on.</summary>
    public async Task<Result> UseAsync(long id, CancellationToken cancellationToken = default)
    {
        var accountId = AccountId();
        var kept = await db.MerchantPayoutAccounts.SingleOrDefaultAsync(a => a.Id == id && a.AccountId == accountId && !a.Archived, cancellationToken);
        if (kept is null)
        {
            return NotFound;
        }

        var chosen = Choose(await AccountAsync(accountId, cancellationToken), kept);
        if (chosen.IsFailure)
        {
            return chosen;
        }

        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>Stops keeping an account; the one payouts go to stays until another is chosen.</summary>
    public async Task<Result> RemoveAsync(long id, CancellationToken cancellationToken = default)
    {
        var accountId = AccountId();
        var kept = await db.MerchantPayoutAccounts.SingleOrDefaultAsync(a => a.Id == id && a.AccountId == accountId && !a.Archived, cancellationToken);
        if (kept is null)
        {
            return NotFound;
        }

        var main = (await AccountAsync(accountId, cancellationToken)).Single(m => m.Id == accountId);
        if (kept.IsInUse(main))
        {
            return Error.Conflict("payoutAccount.inUse", "Payouts go to this account. Choose another one for payouts first.");
        }

        kept.Archive();
        await db.SaveChangesAsync(cancellationToken);

        return Result.Success();
    }

    /// <summary>Sets <paramref name="kept"/> on the main profile, and every business of the account follows it.</summary>
    private static Result Choose(IReadOnlyList<Merchant> businesses, MerchantPayoutAccount kept)
    {
        var main = businesses.Single(m => m.IsMainProfile);
        var set = main.SetPayoutAccount(kept.Method, kept.Number, kept.Name);
        if (set.IsSuccess)
        {
            foreach (var business in businesses.Where(m => !m.IsMainProfile))
            {
                business.FollowAccount(main);
            }
        }

        return set;
    }

    /// <summary>The account's main profile and its businesses, whichever business is being worked in.</summary>
    private Task<List<Merchant>> AccountAsync(long accountId, CancellationToken cancellationToken)
    {
        return db.Merchants
            .IgnoreQueryFilters([QueryFilters.Merchant])
            .Where(m => m.Id == accountId || m.MainMerchantId == accountId)
            .ToListAsync(cancellationToken);
    }

    private long AccountId()
    {
        return currentUser.AccountId ?? throw new InvalidOperationException("A merchant login keeps payout accounts.");
    }
}
