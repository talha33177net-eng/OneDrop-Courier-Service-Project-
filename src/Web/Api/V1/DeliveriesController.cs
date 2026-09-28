using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Authentication;
using Application.Grouping.ShipNow;

namespace Web.Api.V1;

/// <summary>
/// The customer's own deliveries, for the customer app. Authenticated by the customer's sign-in cookie on their
/// operator's subdomain, not by an API key: merchants never reach a delivery.
/// </summary>
[ApiController]
[Route("api/v1/deliveries")]
[Authorize(Policy = Policies.CustomerPortal)]
public class DeliveriesController : ControllerBase
{
    /// <summary>
    /// Ship now: stop waiting for more shops and deliver tomorrow. A delivery that has already closed is a 409;
    /// anyone else's delivery is a 404.
    /// </summary>
    [HttpPost("{number}/ship-now")]
    [ProducesResponseType<ShipNowResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ShipNowResult>> ShipNow(
        string number,
        [FromServices] ShipNowHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(number, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
