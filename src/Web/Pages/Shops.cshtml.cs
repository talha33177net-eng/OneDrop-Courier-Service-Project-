using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Abstractions;
using Application.Merchants.ShopWindow;

namespace Web.Pages;

/// <summary>
/// The operator's shopping window, where the "joined" SMS links: every shop that chose to be listed, each with a link
/// to where customers shop. Open to everyone and the same for everyone, so it names no customer or delivery; the
/// signed-in customer's "My deliveries" shows the same list without the shops already in each delivery.
/// </summary>
public class ShopsModel(ITenantContext tenantContext, ShoppingWindow window) : PageModel
{
    public TenantInfo Tenant { get; private set; } = null!;

    public IReadOnlyList<WindowShop> Shops { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (tenantContext.Tenant is not { } tenant)
        {
            return NotFound();
        }

        Tenant = tenant;
        Shops = await window.ShopsAsync([], cancellationToken);

        return Page();
    }
}
