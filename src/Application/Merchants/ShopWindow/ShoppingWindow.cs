using Microsoft.EntityFrameworkCore;
using Application.Abstractions;

namespace Application.Merchants.ShopWindow;

/// <summary>A shop in the shopping window: its name, where customers shop, and what it sells.</summary>
public sealed record WindowShop(string Name, string Url, string? About);

/// <summary>
/// The operator's shopping window: the shops that chose to be listed (<c>Merchant.ShopUrl</c>), by name. Shown to
/// customers whose delivery is still open, so an order from one more shop rides along for the extra-shop fee. Never
/// shown to a shop, and it names no customer or delivery.
/// </summary>
public class ShoppingWindow(IAppDbContext db)
{
    /// <summary>Every listed shop of the operator but the ones left out (the shops already in a delivery).</summary>
    public async Task<IReadOnlyList<WindowShop>> ShopsAsync(
        IReadOnlyCollection<long> leaveOut,
        CancellationToken cancellationToken = default)
    {
        return await db.Merchants
            .Where(merchant => merchant.ShopUrl != null && !merchant.Archived && !leaveOut.Contains(merchant.Id))
            .OrderBy(merchant => merchant.Name)
            .Select(merchant => new WindowShop(merchant.Name, merchant.ShopUrl!, merchant.ShopAbout))
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }
}
