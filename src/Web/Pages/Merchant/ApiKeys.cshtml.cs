using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Merchants.ApiKeys;

namespace Web.Pages.Merchant;

/// <summary>
/// The shop's API keys for its website: issue a key (shown once, on the answer to the form, never stored or put in a
/// cookie) and revoke one. Only the signed-in shop's own keys.
/// </summary>
public class ApiKeysModel(MerchantApiKeysHandler handler) : PageModel
{
    public IReadOnlyList<ApiKeyRow> Keys { get; private set; } = [];

    [BindProperty]
    public string? Name { get; set; }

    /// <summary>The new key's plaintext, on the page that answers the form only.</summary>
    public string? Issued { get; private set; }

    public string? Problem { get; private set; }

    [TempData]
    public string? Done { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Keys = await handler.ListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostIssueAsync(CancellationToken cancellationToken)
    {
        var issued = await handler.IssueAsync(Name, cancellationToken);
        if (issued.IsSuccess)
        {
            Issued = issued.Value;
            Name = null;
            ModelState.Clear();
        }
        else
        {
            Problem = issued.Error!.Message;
        }

        Keys = await handler.ListAsync(cancellationToken);

        return Page();
    }

    public async Task<IActionResult> OnPostRevokeAsync(string? prefix, CancellationToken cancellationToken)
    {
        var revoked = await handler.RevokeAsync(prefix, cancellationToken);
        if (revoked.IsFailure)
        {
            return NotFound();
        }

        Done = $"Key od_{prefix}_… is revoked. Calls with it are refused from now on.";

        return RedirectToPage();
    }
}
