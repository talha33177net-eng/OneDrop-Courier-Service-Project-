using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Common;
using Domain.Grouping;

namespace Application.Grouping.ShipNow;

/// <summary>The closed delivery and the day it arrives, in the tenant's time zone.</summary>
public sealed record ShipNowResult(string Number, DateOnly DeliveryDate);

/// <summary>
/// Ship now: the signed-in customer closes one of their open deliveries so it goes out the next day instead of
/// waiting for more shops. Another customer's delivery is not found, never forbidden, so its number reveals nothing.
/// </summary>
public class ShipNowHandler(IAppDbContext db, ITenantContext tenantContext, ICurrentUser currentUser, TimeProvider time)
{
    public async Task<Result<ShipNowResult>> HandleAsync(string number, CancellationToken cancellationToken = default)
    {
        var tenant = tenantContext.Tenant ?? throw new InvalidOperationException("Ship now needs a tenant.");
        var customerId = currentUser.CustomerId;
        var group = await db.DeliveryGroups.FirstOrDefaultAsync(
            g => g.Number == number && g.CustomerId == customerId,
            cancellationToken);
        if (group is null)
        {
            return Error.NotFound("deliveryGroup.notFound", $"Delivery {number} was not found.");
        }

        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone);
        var shipped = group.ShipNow(time.GetUtcNow().UtcDateTime, timeZone);
        if (shipped.IsFailure)
        {
            return shipped.Error!;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // The lock job or an order past the deadline locked it since it was read
            return DeliveryGroup.NotOpenForShipNow;
        }

        return new ShipNowResult(
            group.Number,
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(group.LocksAt, timeZone)));
    }
}
