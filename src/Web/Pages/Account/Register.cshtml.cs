using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Application.Abstractions;
using Application.Merchants.Onboarding;
using Application.Network.ListAreas;
using Domain.Merchants;
using Infrastructure.Identity;

namespace Web.Pages.Account;

/// <summary>
/// A shop signs up as a merchant: business details, a login and where parcels are picked up. The account waits for the
/// courier's approval before it can book; the merchant is signed in at once and sees that on its dashboard.
/// </summary>
[EnableRateLimiting("public")]
public class RegisterModel(
    MerchantOnboarding onboarding,
    ListAreasHandler areas,
    SignInManager<AppUser> signInManager,
    ITenantContext tenantContext) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public IReadOnlyList<AreaItem> Areas { get; private set; } = [];

    public string? Problem { get; private set; }

    public string Courier => tenantContext.Tenant?.Name ?? "";

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        if (!tenantContext.HasTenant)
        {
            return NotFound();
        }

        Areas = await areas.HandleAsync(cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (!tenantContext.HasTenant)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            Areas = await areas.HandleAsync(cancellationToken);

            return Page();
        }

        var created = await onboarding.CreateAsync(
            new NewMerchant(
                new MerchantProfile(Input.Business, Input.Owner, Input.Phone, Input.Email, Input.Address),
                Input.Email,
                Input.Password,
                Input.PickupAreaId,
                Input.PickupAddress),
            approved: false,
            cancellationToken);
        if (created.IsFailure)
        {
            Problem = created.Error!.Message;
            Areas = await areas.HandleAsync(cancellationToken);

            return Page();
        }

        var user = await signInManager.UserManager.FindByNameAsync(Input.Email.Trim());
        await signInManager.SignInAsync(user!, isPersistent: false);

        return RedirectToPage("/Merchant/Index");
    }

    public class InputModel
    {
        [Required(ErrorMessage = "Enter your business or page name.")]
        [StringLength(200)]
        public string Business { get; set; } = "";

        [Required(ErrorMessage = "Enter the owner's name.")]
        [StringLength(200)]
        public string Owner { get; set; } = "";

        [Required(ErrorMessage = "Enter a mobile number.")]
        public string Phone { get; set; } = "";

        [Required(ErrorMessage = "Enter your email.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Choose a password.")]
        [StringLength(100, MinimumLength = 8, ErrorMessage = "Use at least 8 characters.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Required(ErrorMessage = "Enter your business address.")]
        [StringLength(500)]
        public string Address { get; set; } = "";

        [Required(ErrorMessage = "Choose the area we pick up from.")]
        public long? PickupAreaId { get; set; }

        [Required(ErrorMessage = "Enter the pickup address.")]
        [StringLength(500)]
        public string PickupAddress { get; set; } = "";
    }
}
