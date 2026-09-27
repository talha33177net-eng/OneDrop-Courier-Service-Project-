using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Authentication;
using Application.Orders.CreateOrder;
using Application.Orders.GetOrder;

namespace Web.Api.V1;

/// <summary>The merchant order API. Authenticated by API key; everything is scoped to the key's merchant.</summary>
[ApiController]
[Route("api/v1/orders")]
[Authorize(Policy = Policies.MerchantApi)]
public class OrdersController : ControllerBase
{
    /// <summary>
    /// Creates an order. Send an Idempotency-Key header to make retries safe: the same key returns the same
    /// order (200) instead of creating a second one (201).
    /// </summary>
    [HttpPost]
    [ProducesResponseType<CreateOrderResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<CreateOrderResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<CreateOrderResult>> Create(
        CreateOrderCommand command,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromServices] CreateOrderHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(command with { IdempotencyKey = idempotencyKey }, cancellationToken);
        if (result.IsFailure)
        {
            return this.ToProblem(result.Error!);
        }

        return result.Value.Replayed
            ? Ok(result.Value)
            : CreatedAtAction(nameof(Get), new { number = result.Value.Number }, result.Value);
    }

    /// <summary>One of the caller's orders. Anyone else's order is a 404, never a 403.</summary>
    [HttpGet("{number}")]
    [ProducesResponseType<OrderDetails>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetails>> Get(
        string number,
        [FromServices] GetOrderHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(number, cancellationToken);

        return result.IsSuccess ? Ok(result.Value) : this.ToProblem(result.Error!);
    }
}
