using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Application.Abstractions;
using Application.Auth.PhoneLogin;
using Application.Common;
using Domain.Customers;
using Infrastructure.Identity;

namespace Web.Pages.Account;

/// <summary>
/// Customer sign in: phone number, then the six-digit code from the SMS. The login (Identity user) is created
/// on the first successful code, never before, so typing someone's number creates nothing.
/// </summary>
[EnableRateLimiting("otp")]
public class PhoneLoginModel(
    PhoneLoginService phoneLogin,
    UserManager<AppUser> users,
    SignInManager<AppUser> signIn,
    ITenantContext tenantContext) : PageModel
{
    [BindProperty]
    public string? Phone { get; set; }

    [BindProperty]
    public string? Code { get; set; }

    public bool CodeSent { get; private set; }

    public IActionResult OnGet()
    {
        return tenantContext.HasTenant ? Page() : RedirectToPage("/Index");
    }

    public async Task<IActionResult> OnPostSendAsync(CancellationToken cancellationToken)
    {
        var sent = await phoneLogin.RequestAsync(Phone, cancellationToken);
        if (sent.IsFailure)
        {
            ModelState.AddModelError(nameof(Phone), sent.Error!.Message);
            return Page();
        }

        CodeSent = true;

        return Page();
    }

    public async Task<IActionResult> OnPostVerifyAsync(CancellationToken cancellationToken)
    {
        CodeSent = true;
        var verified = await phoneLogin.VerifyAsync(Phone, Code, cancellationToken);
        if (verified.IsFailure)
        {
            ModelState.AddModelError(nameof(Code), verified.Error!.Message);
            return Page();
        }

        var customer = verified.Value;
        var user = await users.FindByNameAsync(customer.Phone) ?? await CreateUserAsync(customer);
        if (user is null)
        {
            ModelState.AddModelError(nameof(Code), "We could not create your account. Please try again.");
            return Page();
        }

        await signIn.SignInAsync(user, isPersistent: true);

        return RedirectToPage("/Customer/Index");
    }

    private async Task<AppUser?> CreateUserAsync(Domain.Customers.Customer customer)
    {
        var user = new AppUser
        {
            UserName = customer.Phone,
            PhoneNumber = customer.Phone,
            PhoneNumberConfirmed = true,
            DisplayName = customer.Name ?? PhoneNumber.Parse(customer.Phone).Value.Local,
            TenantId = tenantContext.TenantId,
            CustomerId = customer.Id,
            Created = DateTime.UtcNow
        };

        var created = await users.CreateAsync(user);
        if (created.Succeeded)
        {
            created = await users.AddToRoleAsync(user, Roles.Customer);
        }

        return created.Succeeded ? user : null;
    }
}
