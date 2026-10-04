using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Merchants;
using Domain.Customers;
using Domain.Orders;

namespace Web.Pages.Merchant;

/// <summary>
/// The merchant's own orders. No WHERE MerchantId here on purpose: the merchant filter adds it, and this page
/// is the visible proof that it does.
/// </summary>
public class OrdersModel(IAppDbContext db, ShopDropOffs dropOffs) : PageModel
{
    public string MerchantName { get; private set; } = "";

    public IReadOnlyList<OrderRow> Orders { get; private set; } = [];

    /// <summary>Set while the shop has been late too often and brings its parcels to the hub itself.</summary>
    public OwnDropOff? DropOff { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        MerchantName = await db.Merchants.Select(m => m.Name).FirstOrDefaultAsync(cancellationToken) ?? "";
        DropOff = await dropOffs.OwnAsync(cancellationToken);

        Orders = await (
            from order in db.Orders
            join address in db.CustomerAddresses on order.AddressId equals address.Id
            join area in db.Areas on address.AreaId equals area.Id
            orderby order.Id descending
            select new OrderRow(
                order.Number,
                order.ExternalReference,
                order.RecipientName,
                area.Name,
                order.Status,
                order.Packages.Count,
                order.CodAmount,
                order.Created,
                order.ConfirmedOn == null ? order.CustomerStep : CustomerStep.None))
            .Take(100)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public sealed record OrderRow(
        string Number,
        string? Reference,
        string Recipient,
        string Area,
        OrderStatus Status,
        int Packages,
        decimal Cod,
        DateTime Created,
        CustomerStep Waits);
}
