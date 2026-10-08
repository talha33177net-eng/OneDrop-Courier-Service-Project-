using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Merchants.Businesses;
using Application.Network.ListAreas;
using Infrastructure.Identity;

namespace Web.Pages.Merchant;

/// <summary>
/// The merchant adds another business to its account. It starts work in the new business at once: the sign-in is
/// renewed so the new business is among the ones this login may work in, and the business cookie points at it.
/// </summary>
public class NewBusinessModel(
    MerchantBusinessesHandler businesses,
    ListAreasHandler areas,
    UserManager<AppUser> users,
    SignInManager<AppUser> signInManager) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<AreaItem> Areas { get; private set; } = [];

    public string? Problem { get; private set; }

    public BusinessRoom Room { get; private set; } = new(0, null);

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Areas = await areas.HandleAsync(cancellationToken);
        Room = await businesses.RoomAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var added = await businesses.AddAsync(
                new NewBusiness(Input.Name, Input.Phone, Input.Email, Input.Address, Input.PickupAreaId, Input.PickupAddress),
                cancellationToken);
            if (added.IsSuccess)
            {
                if (await users.GetUserAsync(User) is { } user)
                {
                    await signInManager.RefreshSignInAsync(user);
                }

                BusinessSwitch.Open(Response, User, added.Value);
                TempData["Done"] = $"{Input.Name.Trim()} is added. You are working in it now; switch businesses from the menu at the top.";

                return RedirectToPage("/Merchant/Index");
            }

            Problem = added.Error!.Message;
        }

        Areas = await areas.HandleAsync(cancellationToken);
        Room = await businesses.RoomAsync(cancellationToken);

        return Page();
    }

    public class InputModel
    {
        [Required(ErrorMessage = "Enter the business name.")]
        public string Name { get; set; } = "";

        [Required(ErrorMessage = "Enter a mobile number for this business.")]
        public string Phone { get; set; } = "";

        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string? Email { get; set; }

        [Required(ErrorMessage = "Enter the business address.")]
        public string Address { get; set; } = "";

        [Required(ErrorMessage = "Choose the pickup area.")]
        public long? PickupAreaId { get; set; }

        [Required(ErrorMessage = "Enter the pickup address.")]
        public string PickupAddress { get; set; } = "";
    }
}
