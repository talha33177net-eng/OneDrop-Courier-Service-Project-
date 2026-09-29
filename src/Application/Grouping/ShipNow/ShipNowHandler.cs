using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Common;
using Domain.Grouping;
using Domain.Pricing;

namespace Application.Grouping.ShipNow;

/// <summary>
/// The closed delivery, the day it arrives in the tenant's time zone, and what Ship now added to its fee: the fast
/// difference when it brought the day forward, nothing on the last day to join.
/// </summary>
public sealed record ShipNowResult(string Number, DateOnly DeliveryDate, decimal AddedFee);

/// <summary>
/// Ship now: the signed-in customer closes one of their open deliveries so it goes out the next day instead of
/// waiting for more shops; bringing the day forward costs the fast difference. Another customer's delivery is not
/// found, never forbidden, so its number reveals nothing.
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

        var now = time.GetUtcNow().UtcDateTime;
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(tenant.TimeZone);
        var addedFee = group.ShipNowBringsForward(now, timeZone) ? new DeliveryFeeCalculator(tenant.Fees).ShipNowFee : 0;
        var shipped = group.ShipNow(now, timeZone);
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
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(group.LocksAt, timeZone)),
            addedFee);
    }
}
