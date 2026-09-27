using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Infrastructure.Sms;

namespace Web.Pages.Dev;

/// <summary>Development only: what the fake SMS sender would have sent, so a demo can read login codes.</summary>
public class SmsModel(SmsLog log, IWebHostEnvironment environment) : PageModel
{
    public IReadOnlyList<SentSms> Messages { get; private set; } = [];

    public IActionResult OnGet()
    {
        if (!environment.IsDevelopment())
        {
            return NotFound();
        }

        Messages = log.Recent;

        return Page();
    }
}
