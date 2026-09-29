using Application.Abstractions;

namespace Application.Delivery.PlanTrips;

/// <summary>
/// Plans the tenant's trips for today at every hub. Runs every few minutes from the start of delivery day, so a
/// delivery whose last parcel arrives during the day still goes out with a rider who has not left yet.
/// </summary>
public class PlanTripsJob(TripPlanning planning) : ITenantJob
{
    public Task RunAsync(CancellationToken cancellationToken)
    {
        return planning.PlanAsync(hubId: null, cancellationToken);
    }
}
