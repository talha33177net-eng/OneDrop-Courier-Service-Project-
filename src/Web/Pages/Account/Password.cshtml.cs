using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Infrastructure.Identity;

namespace Web.Pages.Account;

/// <summary>Anyone signed in changes their own password, after giving the one they have now.</summary>
[Authorize]
public class PasswordModel(UserManager<AppUser> users, SignInManager<AppUser> signInManager) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var user = await users.GetUserAsync(User);
        if (user is null)
        {
            return Challenge();
        }

        var changed = await users.ChangePasswordAsync(user, Input.CurrentPassword, Input.NewPassword);
        if (!changed.Succeeded)
        {
            foreach (var error in changed.Errors)
            {
                ModelState.AddModelError(
                    error.Code == nameof(IdentityErrorDescriber.PasswordMismatch) ? "Input.CurrentPassword" : "Input.NewPassword",
                    error.Code == nameof(IdentityErrorDescriber.PasswordMismatch) ? "That is not your current password." : error.Description);
            }

            return Page();
        }

        // A new password changes the security stamp; signing in again keeps this browser signed in
        await signInManager.RefreshSignInAsync(user);
        TempData["Done"] = "Your password is changed. Use the new one next time you sign in.";

        return RedirectToPage();
    }

    public sealed class InputModel
    {
        [Required(ErrorMessage = "Enter the password you sign in with now.")]
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; } = "";

        [Required(ErrorMessage = "Enter a new password.")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; } = "";

        [Required(ErrorMessage = "Type the new password again.")]
        [DataType(DataType.Password)]
        [Compare(nameof(NewPassword), ErrorMessage = "The two new passwords are not the same.")]
        public string ConfirmPassword { get; set; } = "";
    }
}
