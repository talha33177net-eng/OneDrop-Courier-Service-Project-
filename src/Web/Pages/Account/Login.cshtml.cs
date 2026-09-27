using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Abstractions;
using Infrastructure.Identity;

namespace Web.Pages.Account;

/// <summary>
/// Staff sign in. Users are filtered to the current host's tenant, so dhaka.* only finds Dhaka logins and the
/// bare domain only finds platform staff.
/// </summary>
public class LoginModel(SignInManager<AppUser> signInManager, ITenantContext tenantContext) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string Where => tenantContext.Tenant?.Name ?? "the OneDrop platform";

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(
            Input.Email.Trim(),
            Input.Password,
            Input.RememberMe,
            lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            ModelState.AddModelError("", "Too many attempts. Try again in a few minutes.");
            return Page();
        }

        if (!result.Succeeded)
        {
            ModelState.AddModelError("", $"The email or password is not right for {Where}.");
            return Page();
        }

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }

    public class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = "";

        [Required]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Display(Name = "Keep me signed in")]
        public bool RememberMe { get; set; }
    }
}
