using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Application.Common;
using Application.Grouping.ShipNow;
using Domain.Grouping;
using Domain.Orders;

namespace Web.Pages.Customer;

/// <summary>
/// Everything on its way to the signed-in customer, from every shop, and the deliveries still waiting for more
/// shops, each with Ship now. The full delivery view (fee, savings, packages collected) is task 2.8.
/// </summary>
public class IndexModel(IAppDbContext db, ITenantContext tenantContext, ShipNowHandler shipNow) : PageModel
{
    public IReadOnlyList<WaitingDelivery> Waiting { get; private set; } = [];

    public IReadOnlyList<DeliveryRow> Orders { get; private set; } = [];

    [TempData]
    public string? Message { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        var customerId = long.Parse(User.FindFirst(AppClaims.CustomerId)!.Value, CultureInfo.InvariantCulture);
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById(tenantContext.Tenant!.TimeZone);

        var open = await (
            from delivery in db.DeliveryGroups
            join address in db.CustomerAddresses on delivery.AddressId equals address.Id
            join area in db.Areas on address.AreaId equals area.Id
            where delivery.CustomerId == customerId && delivery.Status == DeliveryGroupStatus.Open
            orderby delivery.LocksAt
            select new
            {
                delivery.Number,
                Address = address.Line1 + ", " + area.Name,
                delivery.LocksAt,
                Shops = (
                    from order in db.Orders
                    join merchant in db.Merchants on order.MerchantId equals merchant.Id
                    where order.DeliveryGroupId == delivery.Id
                    select merchant.Name)
                    .Distinct()
                    .ToList()
            })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        // Delivery day starts at LocksAt; the day before it is the last one on which shops can join
        Waiting = [.. open.Select(delivery =>
        {
            var deliveryDay = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(delivery.LocksAt, timeZone));

            return new WaitingDelivery(
                delivery.Number,
                delivery.Address,
                [.. delivery.Shops.Order()],
                deliveryDay.AddDays(-1),
                deliveryDay);
        })];

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

    public async Task<IActionResult> OnPostShipNowAsync(string number, CancellationToken cancellationToken)
    {
        var shipped = await shipNow.HandleAsync(number, cancellationToken);
        Message = shipped.IsSuccess
            ? $"Delivery {shipped.Value.Number} is closed. We deliver it on {shipped.Value.DeliveryDate:dddd d MMMM}."
            : shipped.Error!.Message;

        return RedirectToPage();
    }

    public sealed record WaitingDelivery(
        string Number,
        string Address,
        IReadOnlyList<string> Shops,
        DateOnly LastDayToJoin,
        DateOnly DeliveryDay);

    public sealed record DeliveryRow(string Number, string Shop, string Address, OrderStatus Status, int Packages, decimal Cod);
}
