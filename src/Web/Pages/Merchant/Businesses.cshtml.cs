using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Abstractions;
using Application.Merchants.Businesses;
using Infrastructure.Identity;

namespace Web.Pages.Merchant;

/// <summary>Every business of the merchant's account, each with its own figures, and the switch between them.</summary>
public class BusinessesModel(MerchantBusinessesHandler businesses, ICurrentUser currentUser) : PageModel
{
    public IReadOnlyList<BusinessCard> Cards { get; private set; } = [];

    public long? Current => currentUser.MerchantId;

    public BusinessRoom Room { get; private set; } = new(0, null);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Cards = await businesses.ListAsync(cancellationToken);
        Room = await businesses.RoomAsync(cancellationToken);
    }

    /// <summary>Works in another business of the account from now on; anyone else's business is not found.</summary>
    public async Task<IActionResult> OnPostOpenAsync(long id, string? returnUrl, CancellationToken cancellationToken)
    {
        var business = await businesses.NameOfAsync(id, cancellationToken);
        if (business.IsFailure)
        {
            return NotFound();
        }

        BusinessSwitch.Open(Response, User, id == currentUser.AccountId ? null : id);
        TempData["Done"] = $"You are working in {business.Value} now.";

        return LocalRedirect(returnUrl is not null && Url.IsLocalUrl(returnUrl) ? returnUrl : "/Merchant");
    }
}

/// <summary>Writes the business cookie: the business to work in, or none for the account's main profile.</summary>
public static class BusinessSwitch
{
    public static void Open(HttpResponse response, ClaimsPrincipal user, long? business)
    {
        var name = BusinessCookie.Name(user);
        if (business is null)
        {
            response.Cookies.Delete(name);

            return;
        }

        response.Cookies.Append(name, business.Value.ToString(CultureInfo.InvariantCulture), new CookieOptions
        {
            HttpOnly = true,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromDays(365)
        });
    }
}
