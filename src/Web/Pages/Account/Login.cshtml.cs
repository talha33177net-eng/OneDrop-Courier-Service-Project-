using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Common;
using Infrastructure.Identity;

namespace Web.Pages.Account;

/// <summary>
/// Sign in, for merchants, hub staff, riders and admins. Users are filtered to the current host's tenant, so a courier's
/// site only finds its own logins and the bare domain only finds platform staff.
/// </summary>
public class LoginModel(
    SignInManager<AppUser> signInManager,
    ITenantContext tenantContext,
    IWebHostEnvironment environment,
    IConfiguration configuration) : PageModel
{
    // The order a newcomer tries the demo in
    private static readonly string[] RoleOrder =
        [Roles.Merchant, Roles.TenantAdmin, Roles.HubStaff, Roles.Rider, Roles.PlatformAdmin];

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string Where => tenantContext.Tenant?.Name ?? "the OneDrop platform";

    public bool OnCourierSite => tenantContext.HasTenant;

    /// <summary>Development only: the seeded logins of this host, each signed in with one press.</summary>
    public IReadOnlyList<DemoLogin> DemoLogins { get; private set; } = [];

    /// <summary>The seeded demo password, only ever set in Development.</summary>
    public string? DemoPassword { get; private set; }

    public async Task OnGetAsync()
    {
        await LoadDemoLoginsAsync();
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl)
    {
        if (!ModelState.IsValid)
        {
            await LoadDemoLoginsAsync();

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
            await LoadDemoLoginsAsync();
            return Page();
        }

        if (!result.Succeeded)
        {
            ModelState.AddModelError("", $"The email or password is not right for {Where}.");
            await LoadDemoLoginsAsync();
            return Page();
        }

        // Hub staff choose the hub they work at after each sign-in, never inherit someone else's choice
        Response.Cookies.Delete(Hub.RememberHub.Cookie);

        return LocalRedirect(Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }

    private async Task LoadDemoLoginsAsync()
    {
        var password = configuration["Seed:Password"];
        if (!environment.IsDevelopment() || string.IsNullOrEmpty(password))
        {
            return;
        }

        // The user query filter keeps this to the host's own courier (platform staff on the bare domain)
        var users = await signInManager.UserManager.Users
            .Where(user => user.Email!.EndsWith(".test"))
            .OrderBy(user => user.Email)
            .ToListAsync();
        var logins = new List<DemoLogin>();
        foreach (var user in users)
        {
            var roles = await signInManager.UserManager.GetRolesAsync(user);
            var role = RoleOrder.FirstOrDefault(roles.Contains);
            if (role is not null)
            {
                logins.Add(new DemoLogin(user.Email!, user.DisplayName, role));
            }
        }

        DemoLogins = [.. logins.OrderBy(login => Array.IndexOf(RoleOrder, login.Role))];
        DemoPassword = password;
    }

    public sealed record DemoLogin(string Email, string Name, string Role)
    {
        public string RoleName => Role switch
        {
            Roles.HubStaff => "Hub staff",
            Roles.Rider => "Rider",
            Roles.Merchant => "Merchant",
            Roles.TenantAdmin => "Admin",
            _ => "Platform admin"
        };
    }

    public class InputModel
    {
        [Required(ErrorMessage = "Enter your email.")]
        [EmailAddress(ErrorMessage = "Enter a valid email address.")]
        public string Email { get; set; } = "";

        [Required(ErrorMessage = "Enter your password.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Display(Name = "Keep me signed in")]
        public bool RememberMe { get; set; }
    }
}
