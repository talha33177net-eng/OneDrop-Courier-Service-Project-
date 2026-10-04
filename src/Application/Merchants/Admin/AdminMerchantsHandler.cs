using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Common;
using Application.Merchants.Onboarding;
using Domain.Common;
using Domain.Merchants;
using Domain.Parcels;
using Domain.Payments;

namespace Application.Merchants.Admin;

public sealed record MerchantRow(
    long Id,
    string Name,
    string OwnerName,
    string Phone,
    MerchantStatus Status,
    int Parcels,
    int Delivered,
    int Returned,
    decimal Unpaid,
    DateTime Joined);

public sealed record PickupPointView(long Id, string Name, string Address, string Area, long AreaId, string ContactPhone, bool IsDefault);

public sealed record MerchantView(
    long Id,
    MerchantProfile Profile,
    MerchantStatus Status,
    PayoutMethod? PayoutMethod,
    string? PayoutAccount,
    string? PayoutAccountName,
    string? LoginEmail,
    IReadOnlyList<PickupPointView> PickupPoints,
    int Parcels,
    int Delivered,
    int Returned,
    decimal Unpaid,
    DateTime Joined);

/// <summary>
/// The courier's merchants: list and search them, add one (active at once), approve a sign-up, suspend or reactivate,
/// correct the profile and the payout account. Courier admins only.
/// </summary>
public class AdminMerchantsHandler(
    IAppDbContext db,
    ITenantContext tenantContext,
    MerchantOnboarding onboarding,
    IUserAccounts accounts)
{
    public static readonly Error NotFound = Error.NotFound("merchant.notFound", "That merchant was not found.");

    public async Task<IReadOnlyList<MerchantRow>> ListAsync(
        MerchantStatus? status,
        string? search,
        CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Require();
        var merchants = db.Merchants.Where(m => !m.Archived);
        if (status is { } only)
        {
            merchants = merchants.Where(m => m.Status == only);
        }

        var text = search?.Trim();
        if (!string.IsNullOrEmpty(text))
        {
            var phone = PhoneNumber.Parse(text);
            var value = phone.IsSuccess ? phone.Value.Value : text;
            merchants = merchants.Where(m => m.Name.Contains(text) || m.OwnerName.Contains(text) || m.ContactPhone == value);
        }

        var rows = await merchants
            .OrderBy(m => m.Status)
            .ThenBy(m => m.Name)
            .Select(m => new MerchantRow(
                m.Id,
                m.Name,
                m.OwnerName,
                m.ContactPhone,
                m.Status,
                db.Parcels.Count(p => p.MerchantId == m.Id),
                db.Parcels.Count(p => p.MerchantId == m.Id && (p.Status == ParcelStatus.Delivered || p.Status == ParcelStatus.PartlyDelivered)),
                db.Parcels.Count(p => p.MerchantId == m.Id && p.Status == ParcelStatus.Returned),
                db.LedgerEntries.Where(e => e.MerchantId == m.Id && e.PayoutId == null).Sum(e => (decimal?)e.Amount) ?? 0,
                m.Created))
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => r with { Joined = tenant.Local(r.Joined) })];
    }

    public async Task<Result<MerchantView>> GetAsync(long id, CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Require();
        var merchant = await db.Merchants.AsNoTracking().SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (merchant is null)
        {
            return NotFound;
        }

        var points = await (
            from point in db.PickupPoints
            join area in db.Areas on point.AreaId equals area.Id
            where point.MerchantId == id && !point.Archived
            orderby point.IsDefault descending, point.Name
            select new PickupPointView(point.Id, point.Name, point.Address, area.Name, area.Id, point.ContactPhone, point.IsDefault))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        var counts = await db.Parcels
            .Where(p => p.MerchantId == id)
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Status, g => g.Count, cancellationToken);
        var unpaid = await db.LedgerEntries
            .Where(e => e.MerchantId == id && e.PayoutId == null)
            .SumAsync(e => (decimal?)e.Amount, cancellationToken) ?? 0;

        return new MerchantView(
            merchant.Id,
            new MerchantProfile(merchant.Name, merchant.OwnerName, merchant.ContactPhone, merchant.ContactEmail, merchant.Address),
            merchant.Status,
            merchant.PayoutMethod,
            merchant.PayoutAccount,
            merchant.PayoutAccountName,
            await accounts.MerchantEmailAsync(id, cancellationToken),
            points,
            counts.Values.Sum(),
            counts.GetValueOrDefault(ParcelStatus.Delivered) + counts.GetValueOrDefault(ParcelStatus.PartlyDelivered),
            counts.GetValueOrDefault(ParcelStatus.Returned),
            unpaid,
            tenant.Local(merchant.Created));
    }

    public Task<Result<long>> AddAsync(NewMerchant spec, CancellationToken cancellationToken = default)
    {
        return onboarding.CreateAsync(spec, approved: true, cancellationToken);
    }

    public Task<Result> EditAsync(long id, MerchantProfile profile, CancellationToken cancellationToken = default)
    {
        return ChangeAsync(id, merchant => merchant.Edit(profile), cancellationToken);
    }

    public Task<Result> ApproveAsync(long id, CancellationToken cancellationToken = default)
    {
        return ChangeAsync(id, merchant => merchant.Approve(), cancellationToken);
    }

    public Task<Result> SuspendAsync(long id, CancellationToken cancellationToken = default)
    {
        return ChangeAsync(id, merchant => merchant.Suspend(), cancellationToken);
    }

    public Task<Result> ReactivateAsync(long id, CancellationToken cancellationToken = default)
    {
        return ChangeAsync(id, merchant => merchant.Reactivate(), cancellationToken);
    }

    public Task<Result> SetPayoutAsync(long id, PayoutMethod method, string? account, string? name, CancellationToken cancellationToken = default)
    {
        return ChangeAsync(id, merchant => merchant.SetPayoutAccount(method, account, name), cancellationToken);
    }

    private async Task<Result> ChangeAsync(long id, Func<Merchant, Result> change, CancellationToken cancellationToken)
    {
        var merchant = await db.Merchants.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (merchant is null)
        {
            return NotFound;
        }

        var changed = change(merchant);
        if (changed.IsFailure)
        {
            return changed;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Error.Conflict("merchant.name.taken", "A merchant with this business name already exists.");
        }

        return Result.Success();
    }
}
