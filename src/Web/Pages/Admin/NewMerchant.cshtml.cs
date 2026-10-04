using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Merchants.Admin;
using Application.Merchants.Onboarding;
using Application.Network.ListAreas;
using Domain.Merchants;

namespace Web.Pages.Admin;

/// <summary>The admin adds a merchant with its login and first pickup point; it can book at once.</summary>
public class NewMerchantModel(AdminMerchantsHandler merchants, ListAreasHandler areas) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<AreaItem> Areas { get; private set; } = [];

    public string? Problem { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Areas = await areas.HandleAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (ModelState.IsValid)
        {
            var added = await merchants.AddAsync(
                new NewMerchant(
                    new MerchantProfile(Input.Business, Input.Owner, Input.Phone, Input.Email, Input.Address),
                    Input.Email,
                    Input.Password,
                    Input.PickupAreaId,
                    Input.PickupAddress),
                cancellationToken);
            if (added.IsSuccess)
            {
                TempData["Done"] = $"{Input.Business} is added and can book parcels. They sign in with {Input.Email}.";

                return RedirectToPage("/Admin/Merchant", new { id = added.Value });
            }

            Problem = added.Error!.Message;
        }

        Areas = await areas.HandleAsync(cancellationToken);

        return Page();
    }

    public class InputModel
    {
        [Required(ErrorMessage = "Enter the business name.")]
        public string Business { get; set; } = "";

        [Required(ErrorMessage = "Enter the owner's name.")]
        public string Owner { get; set; } = "";

        [Required(ErrorMessage = "Enter a mobile number.")]
        public string Phone { get; set; } = "";

        [Required(ErrorMessage = "Enter an email to sign in with.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Choose a first password.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Use at least 8 characters.")]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "Enter the business address.")]
        public string Address { get; set; } = "";

        [Required(ErrorMessage = "Choose the pickup area.")]
        public long? PickupAreaId { get; set; }

        [Required(ErrorMessage = "Enter the pickup address.")]
        public string PickupAddress { get; set; } = "";
    }
}
