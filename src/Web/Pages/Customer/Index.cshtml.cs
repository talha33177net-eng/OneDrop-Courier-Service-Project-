using System.Globalization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Common;
using Domain.Orders;

namespace Web.Pages.Customer;

/// <summary>Everything on its way to the signed-in customer, from every shop. Delivery groups arrive in Week 2.</summary>
public class IndexModel(IAppDbContext db) : PageModel
{
    public IReadOnlyList<DeliveryRow> Orders { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var customerId = long.Parse(User.FindFirst(AppClaims.CustomerId)!.Value, CultureInfo.InvariantCulture);

        Orders = await (
            from order in db.Orders
            join merchant in db.Merchants on order.MerchantId equals merchant.Id
            join address in db.CustomerAddresses on order.AddressId equals address.Id
            join area in db.Areas on address.AreaId equals area.Id
            where order.CustomerId == customerId
            orderby order.Id descending
            select new DeliveryRow(
                order.Number,
                merchant.Name,
                address.Line1 + ", " + area.Name,
                order.Status,
                order.Packages.Count,
                order.CodAmount))
            .Take(50)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
    }

    public sealed record DeliveryRow(string Number, string Shop, string Address, OrderStatus Status, int Packages, decimal Cod);
}
