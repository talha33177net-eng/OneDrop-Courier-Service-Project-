using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Merchants.Moderators;
using Domain.Merchants;

namespace Web.Pages.Merchant;

/// <summary>
/// The people who work in this merchant account with a sign-in of their own: add one with the things they may do,
/// change those later, stop one or let them back in, and hand out a first password again. Only the account's owner
/// opens this page; a moderator is refused by <see cref="Web.Authentication.MerchantPermissionFilter"/>.
/// </summary>
public class ModeratorsModel(ModeratorsHandler handler) : PageModel
{
    public IReadOnlyList<ModeratorRow> Moderators { get; private set; } = [];

    [BindProperty]
    public string? Name { get; set; }

    [BindProperty]
    public string? Email { get; set; }

    [BindProperty]
    public string? Phone { get; set; }

    /// <summary>The ticked permissions of the add form, or of the row being changed.</summary>
    [BindProperty]
    public MerchantPermissions[] Permissions { get; set; } = [];

    /// <summary>The password of the person just added or just given a new one: shown once, to hand over.</summary>
    public string? FirstPassword { get; private set; }

    public string? PasswordFor { get; private set; }

    public string? Problem { get; private set; }

    [TempData]
    public string? Done { get; set; }

    /// <summary>The permissions in the order the page offers them, with what each one means.</summary>
    public static IReadOnlyList<(MerchantPermissions Permission, string Label, string What)> Choices { get; } =
    [
        (MerchantPermissions.Dashboard, "Dashboard", "The account's figures on the first page"),
        (MerchantPermissions.Parcels, "Parcels", "See the parcels, their history and their labels"),
        (MerchantPermissions.Booking, "Book parcels", "Book, upload, correct and cancel parcels, and ask for pickups"),
        (MerchantPermissions.Payments, "Payments", "The statement, the payouts and the invoices"),
        (MerchantPermissions.Tools, "Tools", "The fraud check and the rate calculator"),
        (MerchantPermissions.Settings, "Settings", "Profile, payout account, pickup points, businesses, API keys and webhook")
    ];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Moderators = await handler.ListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAddAsync(CancellationToken cancellationToken)
    {
        var added = await handler.AddAsync(new NewModerator(Name, Email, Phone, Chosen()), cancellationToken);
        if (added.IsSuccess)
        {
            FirstPassword = added.Value;
            PasswordFor = Email?.Trim();
            Name = Email = Phone = null;
            ModelState.Clear();
        }
        else
        {
            Problem = added.Error!.Message;
        }

        Moderators = await handler.ListAsync(cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostPermissionsAsync(long id, CancellationToken cancellationToken)
    {
        var changed = await handler.ChangePermissionsAsync(id, Chosen(), cancellationToken);
        if (changed.IsFailure)
        {
            Problem = changed.Error!.Message;
            Moderators = await handler.ListAsync(cancellationToken);

            return Page();
        }

        Done = "What they may do is saved.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostStopAsync(long id, CancellationToken cancellationToken)
    {
        var stopped = await handler.StopAsync(id, cancellationToken);
        if (stopped.IsFailure)
        {
            return NotFound();
        }

        Done = "They are stopped and cannot sign in.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostLetBackInAsync(long id, CancellationToken cancellationToken)
    {
        var restored = await handler.LetBackInAsync(id, cancellationToken);
        if (restored.IsFailure)
        {
            return NotFound();
        }

        Done = "They can sign in again with the same password.";

        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetAsync(long id, CancellationToken cancellationToken)
    {
        var reset = await handler.ResetPasswordAsync(id, cancellationToken);
        Moderators = await handler.ListAsync(cancellationToken);
        if (reset.IsFailure)
        {
            Problem = reset.Error!.Message;

            return Page();
        }

        FirstPassword = reset.Value;
        PasswordFor = Moderators.SingleOrDefault(m => m.Id == id)?.Email;

        return Page();
    }

    private MerchantPermissions Chosen()
    {
        return Permissions.Aggregate(MerchantPermissions.None, (all, one) => all | one);
    }
}
