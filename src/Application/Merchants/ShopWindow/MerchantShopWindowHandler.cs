using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Common;
using Domain.Merchants;

namespace Application.Merchants.ShopWindow;

public sealed record ShopWindowSettings(string MerchantName, string? Url, string? About);

/// <summary>
/// The signed-in shop's place in the operator's shopping window: listed with the address customers shop at and a line
/// on what it sells, or not listed at all. Only the shop itself decides.
/// </summary>
public class MerchantShopWindowHandler(IAppDbContext db, ICurrentUser currentUser)
{
    public async Task<ShopWindowSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        var merchant = await MerchantAsync(cancellationToken);

        return new ShopWindowSettings(merchant.Name, merchant.ShopUrl, merchant.ShopAbout);
    }

    public async Task<Result> ListAsync(string? url, string? about, CancellationToken cancellationToken = default)
    {
        var merchant = await MerchantAsync(cancellationToken);
        var listed = merchant.ListInWindow(url, about);
        if (listed.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return listed;
    }

    public async Task LeaveAsync(CancellationToken cancellationToken = default)
    {
        (await MerchantAsync(cancellationToken)).LeaveWindow();
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>The shop signed in; the merchant filter already hides every other, the id says which is meant.</summary>
    private Task<Merchant> MerchantAsync(CancellationToken cancellationToken)
    {
        var merchantId = currentUser.MerchantId ?? throw new InvalidOperationException("Only a shop has a window place.");

        return db.Merchants.SingleAsync(m => m.Id == merchantId, cancellationToken);
    }
}
