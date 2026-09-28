using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Common;
using Domain.Orders;

namespace Application.Orders.GetOrder;

public sealed record OrderDetails(
    long Id,
    string Number,
    string? ExternalReference,
    OrderStatus Status,
    DeliverySpeed Speed,
    bool DoNotHold,
    string RecipientName,
    string Area,
    string Zone,
    decimal CodAmount,
    decimal DeclaredValue,
    decimal Fee,
    DateTime Created,
    IReadOnlyList<PackageDetails> Packages,
    IReadOnlyList<StatusDetails> History);

public sealed record PackageDetails(int Sequence, string Label, string Description, int WeightGrams);

public sealed record StatusDetails(OrderStatus Status, string? Note, DateTime On);

/// <summary>
/// Reads one order by its number. Relies on the query filters: another tenant's or another merchant's order
/// is simply not found, which the API reports as 404 so the caller cannot even learn it exists.
/// </summary>
public class GetOrderHandler(IAppDbContext db)
{
    public async Task<Result<OrderDetails>> HandleAsync(string number, CancellationToken cancellationToken = default)
    {
        var details = await (
            from order in db.Orders
            join address in db.CustomerAddresses on order.AddressId equals address.Id
            join area in db.Areas on address.AreaId equals area.Id
            join zone in db.Zones on area.ZoneId equals zone.Id
            where order.Number == number
            select new OrderDetails(
                order.Id,
                order.Number,
                order.ExternalReference,
                order.Status,
                order.Speed,
                order.DoNotHold,
                order.RecipientName,
                area.Name,
                zone.Name,
                order.CodAmount,
                order.DeclaredValue,
                order.AddedFee,
                order.Created,
                order.Packages
                    .OrderBy(p => p.Sequence)
                    .Select(p => new PackageDetails(
                        p.Sequence,
                        order.Number + "-" + p.Sequence,
                        p.Description,
                        p.WeightGrams))
                    .ToList(),
                order.History
                    .OrderBy(h => h.Id)
                    .Select(h => new StatusDetails(h.Status, h.Note, h.Created))
                    .ToList()))
            .AsNoTracking()
            .FirstOrDefaultAsync(cancellationToken);

        if (details is null)
        {
            return Error.NotFound("order.notFound", $"Order {number} was not found.");
        }

        return details;
    }
}
