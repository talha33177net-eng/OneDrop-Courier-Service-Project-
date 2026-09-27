using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Web.Authentication;
using Application.Network.ListAreas;

namespace Web.Api.V1;

/// <summary>The area list a merchant's checkout shows. An order's address must name one of these.</summary>
[ApiController]
[Route("api/v1/areas")]
[Authorize(Policy = Policies.MerchantApi)]
public class AreasController : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<AreaItem>> List([FromServices] ListAreasHandler handler, CancellationToken cancellationToken)
    {
        return await handler.HandleAsync(cancellationToken);
    }
}
