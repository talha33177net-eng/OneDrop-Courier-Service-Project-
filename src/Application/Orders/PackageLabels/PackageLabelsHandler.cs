using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Orders;

namespace Application.Orders.PackageLabels;

/// <summary>
/// What is printed on one parcel. <see cref="DestinationHub"/> is the hub code the parcel is sorted to; nothing on
/// it names the customer's delivery or the other shops in it (merchant privacy).
/// </summary>
public sealed record PrintedLabel(
    PackageLabel Code,
    int PackagesInOrder,
    string Merchant,
    string Recipient,
    string Area,
    string DestinationHub,
    decimal Cod,
    bool Urgent);

/// <summary>
/// The labels to print, newest order first. <see cref="More"/> says that older orders were left out: at most
/// <see cref="PackageLabelsHandler.MaxOrders"/> orders are printed at once.
/// </summary>
public sealed record PackageLabelSheet(IReadOnlyList<PrintedLabel> Labels, bool More);

/// <summary>
/// Labels for the merchant's own parcels: the orders named, or every order still waiting for its pickup. The merchant
/// query filter keeps other merchants' orders out, so a number that is not theirs prints nothing.
/// </summary>
public class PackageLabelsHandler(IAppDbContext db)
{
    public const int MaxOrders = 100;

    public async Task<PackageLabelSheet> HandleAsync(
        IReadOnlyCollection<string> orderNumbers,
        CancellationToken cancellationToken = default)
    {
        var numbers = orderNumbers.Select(number => number.Trim().ToUpperInvariant()).Distinct().ToList();

        var orders = db.Orders.AsQueryable();
        orders = numbers.Count > 0
            ? orders.Where(order => numbers.Contains(order.Number))
            : orders.Where(order => order.Status == OrderStatus.Created);

        var rows = await (
            from order in orders
            join merchant in db.Merchants on order.MerchantId equals merchant.Id
            join address in db.CustomerAddresses on order.AddressId equals address.Id
            join area in db.Areas on address.AreaId equals area.Id
            orderby order.Id descending
            select new
            {
                order.Number,
                Packages = order.Packages.Count,
                Merchant = merchant.Name,
                order.RecipientName,
                Area = area.Name,
                Hub = area.Zone!.Hub!.Code,
                order.CodAmount,
                Urgent = order.Speed == DeliverySpeed.Fast || order.DoNotHold
            })
            .Take(MaxOrders + 1)
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new PackageLabelSheet(
            [
                .. rows.Take(MaxOrders).SelectMany(row => Enumerable.Range(1, row.Packages).Select(sequence =>
                    new PrintedLabel(
                        new PackageLabel(row.Number, sequence),
                        row.Packages,
                        row.Merchant,
                        row.RecipientName,
                        row.Area,
                        row.Hub,
                        row.CodAmount,
                        row.Urgent)))
            ],
            rows.Count > MaxOrders);
    }
}
