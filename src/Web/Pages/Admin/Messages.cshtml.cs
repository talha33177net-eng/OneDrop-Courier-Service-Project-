using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Notifications.FailedMessages;

namespace Web.Pages.Admin;

/// <summary>
/// The operator's texts and webhooks that have not gone out: still being retried, or given up after every attempt and
/// waiting for someone to send them again once the cause is fixed. Only this operator's messages.
/// </summary>
public class MessagesModel(FailedMessagesHandler handler) : PageModel
{
    public IReadOnlyList<FailedMessage> Messages { get; private set; } = [];

    [TempData]
    public string? Done { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        Messages = await handler.ListAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostSendAgainAsync(long id, CancellationToken cancellationToken)
    {
        var again = await handler.SendAgainAsync(id, cancellationToken);
        if (again.IsFailure && again.Error == FailedMessagesHandler.NotFound)
        {
            return NotFound();
        }

        Done = again.IsSuccess ? "Sent again: it goes out within a few seconds if the cause is fixed." : again.Error!.Message;

        return RedirectToPage();
    }
}
