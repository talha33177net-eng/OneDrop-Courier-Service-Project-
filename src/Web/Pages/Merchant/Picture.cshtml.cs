using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Merchants.Account;

namespace Web.Pages.Merchant;

/// <summary>A business's picture, for a login of the account that owns it; anyone else's is not found.</summary>
public class PictureModel(MerchantPictureHandler pictures) : PageModel
{
    public async Task<IActionResult> OnGetAsync(long id, CancellationToken cancellationToken)
    {
        var picture = await pictures.GetAsync(id, cancellationToken);
        if (picture is null)
        {
            return NotFound();
        }

        Response.Headers.CacheControl = "private, max-age=86400";
        Response.Headers["X-Content-Type-Options"] = "nosniff";

        return File(picture.Content, picture.ContentType);
    }
}
