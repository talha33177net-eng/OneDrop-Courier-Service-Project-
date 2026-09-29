using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Web.Pages.Hub;

/// <summary>
/// Remembers the hub staff last worked at, so the hub's step bar and "Hub today" open on it without asking again.
/// Only a preference: every hub page still checks the hub is this operator's, and the cookie is only written after a
/// page with that hub has rendered. Host-only, like the sign-in cookie, so it stays with its operator.
/// </summary>
public sealed class RememberHub : IAsyncPageFilter
{
    public const string Cookie = "hub";

    /// <summary>The hub the request names, else the one remembered, or null.</summary>
    public static string? Of(HttpContext context)
    {
        var hub = context.Request.Query["hub"].ToString();
        if (hub.Length == 0)
        {
            hub = context.Request.Cookies[Cookie] ?? "";
        }

        return hub.Length is > 0 and <= 10 && hub.All(char.IsLetterOrDigit) ? hub.ToUpperInvariant() : null;
    }

    public Task OnPageHandlerSelectionAsync(PageHandlerSelectedContext context)
    {
        return Task.CompletedTask;
    }

    public async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context, PageHandlerExecutionDelegate next)
    {
        var executed = await next();
        var hub = context.HttpContext.Request.Query["hub"].ToString();
        if (executed.Result is PageResult && hub.Length is > 0 and <= 10 && hub.All(char.IsLetterOrDigit))
        {
            context.HttpContext.Response.Cookies.Append(Cookie, hub.ToUpperInvariant(), new CookieOptions
            {
                HttpOnly = true,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromDays(30)
            });
        }
    }
}
