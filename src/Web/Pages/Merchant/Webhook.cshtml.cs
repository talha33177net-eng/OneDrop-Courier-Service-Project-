using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Merchants.Webhook;

namespace Web.Pages.Merchant;

/// <summary>
/// Where the shop's website hears about its orders: the webhook address, the secret to check signatures with, and a
/// test message. Only the signed-in shop's own settings.
/// </summary>
public class WebhookModel(MerchantWebhookHandler handler) : PageModel
{
    public WebhookSettings Settings { get; private set; } = new("", null, null);

    [BindProperty]
    public string? Address { get; set; }

    public string? Problem { get; private set; }

    [TempData]
    public string? Done { get; set; }

    [TempData]
    public bool DoneOk { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Settings = await handler.GetAsync(cancellationToken);
        Address = Settings.Url;
    }

    public async Task<IActionResult> OnPostSaveAsync(CancellationToken cancellationToken)
    {
        var saved = await handler.SaveAsync(Address, cancellationToken);
        if (saved.IsFailure)
        {
            Problem = saved.Error!.Message;
            Settings = await handler.GetAsync(cancellationToken);

            return Page();
        }

        return Finished("Saved. Your order status changes now go to this address.", ok: true);
    }

    public async Task<IActionResult> OnPostSecretAsync(CancellationToken cancellationToken)
    {
        await handler.NewSecretAsync(cancellationToken);

        return Finished("A new secret is made. Put it in your website now: the old one no longer checks.", ok: true);
    }

    public async Task<IActionResult> OnPostRemoveAsync(CancellationToken cancellationToken)
    {
        await handler.RemoveAsync(cancellationToken);

        return Finished("Removed. Order status changes are no longer sent to your website.", ok: true);
    }

    public async Task<IActionResult> OnPostTestAsync(CancellationToken cancellationToken)
    {
        var sent = await handler.SendTestAsync(cancellationToken);
        if (sent.IsFailure)
        {
            return Finished(sent.Error!.Message, ok: false);
        }

        var response = sent.Value;

        return response.Delivered
            ? Finished($"Test sent. Your website answered {response.StatusCode}, so it works.", ok: true)
            : Finished($"Test not delivered: {response.Describe}. We only count an answer from 200 to 299.", ok: false);
    }

    private RedirectToPageResult Finished(string message, bool ok)
    {
        Done = message;
        DoneOk = ok;

        return RedirectToPage();
    }
}
