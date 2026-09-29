using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Application.Orders.ConfirmOrder;
using Domain.Payments;

namespace Web.Pages.Customer;

/// <summary>
/// Where the SMS link lands: the customer confirms one order with one tap, or pays its delivery fee in advance by
/// bKash or Nagad. No sign-in: the token in the link is the key and names no customer. A used-up or unknown token is a
/// 404, so a guessed link tells nobody anything.
/// </summary>
public class OrderModel(ConfirmOrderHandler handler) : PageModel
{
    public OrderToConfirm? Order { get; private set; }

    public string? Problem { get; private set; }

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        return await ShowAsync(await handler.FindAsync(Token ?? "", cancellationToken));
    }

    public async Task<IActionResult> OnPostConfirmAsync(CancellationToken cancellationToken)
    {
        return await ShowAsync(await handler.ConfirmAsync(Token ?? "", cancellationToken));
    }

    public async Task<IActionResult> OnPostPayAsync(PaymentMethod method, CancellationToken cancellationToken)
    {
        return await ShowAsync(await handler.RequestAdvanceAsync(Token ?? "", method, cancellationToken));
    }

    public async Task<IActionResult> OnPostCheckAsync(CancellationToken cancellationToken)
    {
        return await ShowAsync(await handler.CheckAdvanceAsync(Token ?? "", cancellationToken));
    }

    /// <summary>Shows the order, or the page's own 404 when the link is not valid any more.</summary>
    private async Task<IActionResult> ShowAsync(Domain.Common.Result<OrderToConfirm> result)
    {
        if (result.IsSuccess)
        {
            Order = result.Value;

            return Page();
        }

        if (result.Error!.Type == Domain.Common.ErrorType.NotFound)
        {
            return NotFound();
        }

        Problem = result.Error.Message;
        var again = await handler.FindAsync(Token ?? "", CancellationToken.None);
        Order = again.IsSuccess ? again.Value : null;

        return Order is null ? NotFound() : Page();
    }
}
