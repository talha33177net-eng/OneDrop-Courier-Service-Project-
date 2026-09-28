using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Authentication;
using Application.Pricing.GetQuote;

namespace Web.Api.V1;

/// <summary>The delivery fee for the merchant's checkout, before the order is sent.</summary>
[ApiController]
[Route("api/v1/quote")]
[Authorize(Policy = Policies.MerchantApi)]
public class QuoteController : ControllerBase
{
    /// <summary>
    /// What an order for this phone and address would add to the customer's delivery fee now: the base fee for a
    /// new delivery, or the extra-shop fee (<c>joinsDelivery</c> true) when one is already on its way.
    /// Example: <c>GET /api/v1/quote?phone=01712345678&amp;area=Mirpur 10&amp;line1=House 12, Road 5</c>.
    /// </summary>
    [HttpGet]
    [ProducesResponseType<QuoteResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<QuoteResult>> Get(
        [FromQuery] GetQuoteQuery query,
        [FromServices] GetQuoteHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(query, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
