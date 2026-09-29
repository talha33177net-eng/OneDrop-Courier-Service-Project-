using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Customers;
using Domain.Orders;

namespace Web.Pages.Merchant;

/// <summary>
/// The merchant's own orders. No WHERE MerchantId here on purpose: the merchant filter adds it, and this page
/// is the visible proof that it does.
/// </summary>
public class OrdersModel(IAppDbContext db) : PageModel
{
    public string MerchantName { get; private set; } = "";

    public IReadOnlyList<OrderRow> Orders { get; private set; } = [];

    public IReadOnlyList<KeyRow> Keys { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        MerchantName = await db.Merchants.Select(m => m.Name).FirstOrDefaultAsync(cancellationToken) ?? "";

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

        Keys = await db.MerchantApiKeys
            .OrderBy(k => k.Id)
            .Select(k => new KeyRow(k.Name, k.Prefix, k.LastUsedOn, k.RevokedOn == null))
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

    public sealed record KeyRow(string Name, string Prefix, DateTime? LastUsed, bool Active);
}
