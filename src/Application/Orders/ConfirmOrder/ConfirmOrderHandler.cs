using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Common;
using Domain.Customers;
using Domain.Grouping;
using Domain.Orders;
using Domain.Payments;
using Domain.Pricing;

namespace Application.Orders.ConfirmOrder;

/// <summary>
/// An order the customer has been asked about, as the SMS link opens it: the shop and the day it arrives, what it
/// waits for, and for an advance the fee to pay and the wallet link already asked for (the QR or button they open).
/// </summary>
public sealed record OrderToConfirm(
    string Number,
    string Shop,
    string Delivery,
    DateOnly DeliveryDate,
    decimal CodAmount,
    CustomerStep Step,
    bool Done,
    decimal Fee = 0,
    PaymentMethod? Method = null,
    string? PaymentLink = null);

/// <summary>
/// The customer answering the SMS link for one order: a **one-tap confirmation** for a first cash-on-delivery order,
/// or the **delivery fee in advance** when a refusal, a no-show or the shop's request asks for it (the order is not
/// collected from the shop until it is paid). The token in the link is the only key: it names no customer and cannot
/// be guessed, so an unknown or used-up token is simply not found. The advance is the delivery's first-shop fee, paid
/// once: other shops' orders in the same delivery stop waiting with it, and the rest is paid at the door.
/// </summary>
public class ConfirmOrderHandler(
    IAppDbContext db,
    ITenantContext tenantContext,
    IPaymentGateway gateway,
    TimeProvider time)
{
    public async Task<Result<OrderToConfirm>> FindAsync(string token, CancellationToken cancellationToken = default)
    {
        var found = await FindOrderAsync(token, cancellationToken);

        return found.IsFailure ? found.Error! : await DescribeAsync(found.Value, cancellationToken);
    }

    /// <summary>The customer taps "Yes, send it": the shop may send the order. An order already confirmed is unchanged.</summary>
    public async Task<Result<OrderToConfirm>> ConfirmAsync(string token, CancellationToken cancellationToken = default)
    {
        var found = await FindOrderAsync(token, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var confirmed = found.Value.Confirm(time.GetUtcNow().UtcDateTime);
        if (confirmed.IsFailure)
        {
            return confirmed.Error!;
        }

        await db.SaveChangesAsync(cancellationToken);

        return await DescribeAsync(found.Value, cancellationToken);
    }

    /// <summary>
    /// Asks the wallet for the advance and gives the link to pay it. Asking again for the same wallet gives the same
    /// link; the other wallet's unpaid request is dropped.
    /// </summary>
    public async Task<Result<OrderToConfirm>> RequestAdvanceAsync(
        string token,
        PaymentMethod method,
        CancellationToken cancellationToken = default)
    {
        if (method is not (PaymentMethod.Bkash or PaymentMethod.Nagad))
        {
            return Error.Validation("order.advance.method", "The advance is paid by bKash or Nagad.");
        }

        var found = await FindOrderAsync(token, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var order = found.Value;
        if (!order.WaitsForAdvance)
        {
            return await DescribeAsync(order, cancellationToken);
        }

        var pending = await PendingAdvancesAsync(order.DeliveryGroupId, cancellationToken);
        if (pending.FirstOrDefault(payment => payment.Method == method) is null)
        {
            foreach (var other in pending)
            {
                other.Cancel();
            }

            var fee = await FeeAsync(order, cancellationToken);
            var payment = Payment.InAdvance(order.CustomerId, order.DeliveryGroupId, method, fee);
            var request = await gateway.RequestAsync(
                method,
                fee,
                Tenant.CurrencyCode,
                $"OneDrop delivery fee for {order.Number}",
                cancellationToken);
            payment.RequestedAs(request.Reference, request.Link);
            db.Payments.Add(payment);
            await db.SaveChangesAsync(cancellationToken);
        }

        return await DescribeAsync(order, cancellationToken);
    }

    /// <summary>
    /// Asks the wallet whether the advance has arrived. Once it has, every order of the delivery waiting for it may be
    /// sent, and the fee at the door is what is left.
    /// </summary>
    public async Task<Result<OrderToConfirm>> CheckAdvanceAsync(string token, CancellationToken cancellationToken = default)
    {
        var found = await FindOrderAsync(token, cancellationToken);
        if (found.IsFailure)
        {
            return found.Error!;
        }

        var order = found.Value;
        var pending = await PendingAdvancesAsync(order.DeliveryGroupId, cancellationToken);
        var paid = pending.FirstOrDefault(payment => payment.Status == PaymentStatus.Paid);
        foreach (var payment in pending.Where(payment => payment.Status == PaymentStatus.Pending))
        {
            if (await gateway.IsPaidAsync(payment.Method, payment.GatewayReference!, cancellationToken))
            {
                payment.MarkPaid(time.GetUtcNow().UtcDateTime);
                paid ??= payment;
            }
        }

        if (paid is null)
        {
            return Error.Conflict(
                "order.advance.unpaid",
                $"We have not received the {order.Number} delivery fee yet. Finish paying, then check again.");
        }

        var now = time.GetUtcNow().UtcDateTime;
        var waiting = await db.Orders
            .IgnoreQueryFilters([QueryFilters.Merchant])
            .Where(o => o.DeliveryGroupId == order.DeliveryGroupId && o.ConfirmedOn == null)
            .ToListAsync(cancellationToken);
        foreach (var sibling in waiting)
        {
            sibling.AdvancePaid(now);
        }

        await db.SaveChangesAsync(cancellationToken);

        return await DescribeAsync(order, cancellationToken);
    }

    private TenantInfo Tenant => tenantContext.Tenant ?? throw new InvalidOperationException("Confirming needs a tenant.");

    /// <summary>
    /// The order the link names, whichever shop it belongs to. An order that waits for nothing, or a token that is not
    /// one of the tenant's, is not found: the link says nothing about a customer.
    /// </summary>
    private async Task<Result<Order>> FindOrderAsync(string token, CancellationToken cancellationToken)
    {
        var order = await db.Orders
            .IgnoreQueryFilters([QueryFilters.Merchant])
            .FirstOrDefaultAsync(o => o.CustomerToken == token, cancellationToken);

        return order is null || order.CustomerStep == CustomerStep.None
            ? Error.NotFound("order.confirm.notFound", "This link is not valid any more.")
            : order;
    }

    private async Task<IReadOnlyList<Payment>> PendingAdvancesAsync(long deliveryGroupId, CancellationToken cancellationToken)
    {
        return await db.Payments
            .Where(payment => payment.DeliveryGroupId == deliveryGroupId &&
                payment.Purpose == PaymentPurpose.Advance &&
                payment.Status != PaymentStatus.Cancelled)
            .OrderBy(payment => payment.Id)
            .ToListAsync(cancellationToken);
    }

    /// <summary>The delivery's first-shop fee: what an advance pays, whatever the other shops add at the door.</summary>
    private async Task<decimal> FeeAsync(Order order, CancellationToken cancellationToken)
    {
        var delivery = await db.DeliveryGroups.AsNoTracking()
            .SingleAsync(group => group.Id == order.DeliveryGroupId, cancellationToken);
        var lines = await db.Orders
            .IgnoreQueryFilters([QueryFilters.Merchant])
            .Where(o => o.DeliveryGroupId == order.DeliveryGroupId)
            .Select(o => new { o.MerchantId, o.Speed, o.Status, Weight = o.Packages.Sum(p => p.WeightGrams) })
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return new DeliveryFeeCalculator(Tenant.Fees).FirstShopFee(
            lines.Select(line => new FeeLine(line.MerchantId, line.Speed, line.Status, line.Weight)),
            delivery.Kind);
    }

    private async Task<OrderToConfirm> DescribeAsync(Order order, CancellationToken cancellationToken)
    {
        var about = await (
            from o in db.Orders.IgnoreQueryFilters([QueryFilters.Merchant])
            join merchant in db.Merchants.IgnoreQueryFilters([QueryFilters.Merchant]) on o.MerchantId equals merchant.Id
            join delivery in db.DeliveryGroups on o.DeliveryGroupId equals delivery.Id
            where o.Id == order.Id
            select new { Shop = merchant.Name, delivery.Number, delivery.LocksAt })
            .AsNoTracking()
            .SingleAsync(cancellationToken);
        var pending = order.WaitsForAdvance
            ? (await PendingAdvancesAsync(order.DeliveryGroupId, cancellationToken))
                .FirstOrDefault(payment => payment.Status == PaymentStatus.Pending)
            : null;

        return new OrderToConfirm(
            order.Number,
            about.Shop,
            about.Number,
            DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(
                about.LocksAt,
                TimeZoneInfo.FindSystemTimeZoneById(Tenant.TimeZone))),
            order.CodAmount,
            order.CustomerStep,
            !order.WaitsForCustomer,
            pending?.Fee ?? (order.WaitsForAdvance ? await FeeAsync(order, cancellationToken) : 0),
            pending?.Method,
            pending?.PaymentLink);
    }
}
