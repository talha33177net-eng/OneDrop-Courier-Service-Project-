using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Common;
using Domain.Delivery;

namespace Application.Delivery.Capacities;

/// <summary>One kind of vehicle: what it carries at once (null when the courier has set no limit) and who rides one.</summary>
public sealed record CapacityRow(Vehicle Vehicle, int? MaxParcels, int? MaxLoadGrams, int? MaxParcelGrams, int Riders);

/// <summary>
/// What each kind of vehicle carries at once, the courier's own numbers: the hub never hands a rider more, and sends a
/// big enough vehicle for each pickup. Courier admins change them; a change applies to the next assignment.
/// </summary>
public class VehicleCapacitiesHandler(IAppDbContext db)
{
    public async Task<IReadOnlyList<CapacityRow>> ListAsync(CancellationToken cancellationToken = default)
    {
        var capacities = await db.VehicleCapacities.AsNoTracking().ToDictionaryAsync(c => c.Vehicle, cancellationToken);
        var riders = await db.Riders
            .Where(r => !r.Archived)
            .GroupBy(r => r.Vehicle)
            .Select(g => new { Vehicle = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.Vehicle, g => g.Count, cancellationToken);

        return
        [
            .. Vehicles.All.Select(vehicle => capacities.GetValueOrDefault(vehicle) is { } capacity
                ? new CapacityRow(vehicle, capacity.MaxParcels, capacity.MaxLoadGrams, capacity.MaxParcelGrams, riders.GetValueOrDefault(vehicle))
                : new CapacityRow(vehicle, null, null, null, riders.GetValueOrDefault(vehicle)))
        ];
    }

    public async Task<Result> ChangeAsync(Vehicle vehicle, CapacityValues values, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(vehicle))
        {
            return Error.Validation("capacity.vehicle", "Choose a vehicle.");
        }

        var capacity = await db.VehicleCapacities.SingleOrDefaultAsync(c => c.Vehicle == vehicle, cancellationToken);
        if (capacity is null)
        {
            var created = VehicleCapacity.Create(vehicle, values);
            if (created.IsFailure)
            {
                return created.Error!;
            }

            db.VehicleCapacities.Add(created.Value);
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success();
        }

        var changed = capacity.Change(values);
        if (changed.IsSuccess)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return changed;
    }
}
