using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Abstractions;
using Infrastructure.Email;

namespace Web.Pages.Dev;

/// <summary>
/// Development only: the emails the courier has sent merchants, so a demo can read them without a mailbox. With a
/// mail server configured they are really sent as well, and the button here proves the server answers.
/// </summary>
public class EmailsModel(EmailLog log, EmailOptions options, IEmailSender sender, IWebHostEnvironment environment) : PageModel
{
    public IReadOnlyList<SentEmail> Messages { get; private set; } = [];

    public bool Live => options.Configured;

    public string? MailServer => options.Host;

    [TempData]
    public string? Done { get; set; }

    public string? Problem { get; private set; }

    public IActionResult OnGet()
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        Messages = log.Recent;

        return Page();
    }

    /// <summary>Sends one email to the courier's own address, the quickest way to see the mail server works.</summary>
    public async Task<IActionResult> OnPostTestAsync(CancellationToken cancellationToken)
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        var to = options.From ?? "nobody@example.com";
        try
        {
            await sender.SendAsync(
                new EmailMessage(
                    to,
                    options.FromName,
                    "Test email from the courier panel",
                    "<p>This is a test email from the courier panel. The mail server works.</p>",
                    "This is a test email from the courier panel. The mail server works."),
                cancellationToken);
            Done = Live ? $"Test email sent to {to}." : "No mail server is configured, so the test email was kept here.";

            return RedirectToPage();
        }
        catch (Exception exception)
        {
            Problem = exception.Message;
            Messages = log.Recent;

            return Page();
        }
    }
}
