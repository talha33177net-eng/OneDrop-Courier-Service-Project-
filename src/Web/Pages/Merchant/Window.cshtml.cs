using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Merchants.ShopWindow;

namespace Web.Pages.Merchant;

/// <summary>
/// The shop's place in the operator's shopping window: the address customers shop at and a line on what it sells, or
/// not listed. Only the signed-in shop's own settings.
/// </summary>
public class WindowModel(MerchantShopWindowHandler handler) : PageModel
{
    public ShopWindowSettings Settings { get; private set; } = new("", null, null);

    [BindProperty]
    public string? Address { get; set; }

    [BindProperty]
    public string? About { get; set; }

    public string? Problem { get; private set; }

    [TempData]
    public string? Done { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Settings = await handler.GetAsync(cancellationToken);
        Address = Settings.Url;
        About = Settings.About;
    }

    public async Task<IActionResult> OnPostListAsync(CancellationToken cancellationToken)
    {
        var listed = await handler.ListAsync(Address, About, cancellationToken);
        if (listed.IsFailure)
        {
            Problem = listed.Error!.Message;
            Settings = await handler.GetAsync(cancellationToken);

            return Page();
        }

        Done = "Saved. Customers with a delivery still open now see your shop.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLeaveAsync(CancellationToken cancellationToken)
    {
        await handler.LeaveAsync(cancellationToken);
        Done = "Removed. Your shop is no longer in the shopping window.";

        return RedirectToPage();
    }
}
