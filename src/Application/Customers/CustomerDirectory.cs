using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Customers;
using Domain.Delivery;
using Domain.Grouping;
using Domain.Orders;

namespace Application.Customers;

/// <summary>
/// Recognises the same customer across shops. A phone number is one customer per tenant and a normalised
/// address is one row per customer and area; both are enforced by unique indexes, so when two orders for a
/// new customer arrive at the same moment the loser of the insert race reloads the winner's row.
/// </summary>
public class CustomerDirectory(IAppDbContext db)
{
    public async Task<Customer> FindOrCreateAsync(PhoneNumber phone, string? name, CancellationToken cancellationToken)
    {
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Phone == phone.Value, cancellationToken);
        if (customer is not null)
        {
            customer.RememberName(name);

            return customer;
        }

        return await InsertOrReloadAsync(
            new Customer(phone, name),
            () => db.Customers.FirstOrDefaultAsync(c => c.Phone == phone.Value, cancellationToken),
            cancellationToken);
    }

    public async Task<CustomerAddress> FindOrCreateAddressAsync(
        Customer customer,
        long areaId,
        string line1,
        string? line2,
        string? landmark,
        CancellationToken cancellationToken)
    {
        var matchKey = CustomerAddress.BuildMatchKey(line1, line2);
        var address = await db.CustomerAddresses.FirstOrDefaultAsync(
            a => a.CustomerId == customer.Id && a.AreaId == areaId && a.MatchKey == matchKey,
            cancellationToken);

        return address ?? await InsertOrReloadAsync(
            new CustomerAddress(customer.Id, areaId, line1, line2, landmark),
            () => db.CustomerAddresses.FirstOrDefaultAsync(
                a => a.CustomerId == customer.Id && a.AreaId == areaId && a.MatchKey == matchKey,
                cancellationToken),
            cancellationToken);
    }

    private async Task<T> InsertOrReloadAsync<T>(T entity, Func<Task<T?>> reload, CancellationToken cancellationToken)
        where T : class
    {
        db.Entry(entity).State = EntityState.Added;
        try
        {
            await db.SaveChangesAsync(cancellationToken);

            return entity;
        }
        catch (DbUpdateException)
        {
            db.Entry(entity).State = EntityState.Detached;

            // Someone else inserted the same row first; if it is not there, the failure was something else
            var existing = await reload();
            if (existing is null)
            {
                throw;
            }

            return existing;
        }
    }

    /// <summary>
    /// The customer's record so far: deliveries they accepted, and failed visits (a stop refused or with nobody home,
    /// or an order refused at the door). A customer not saved yet has none.
    /// </summary>
    public async Task<CustomerStanding> StandingAsync(Customer customer, CancellationToken cancellationToken)
    {
        if (customer.IsNew)
        {
            return CustomerStanding.New;
        }

        var accepted = await db.DeliveryGroups
            .CountAsync(g => g.CustomerId == customer.Id && g.Status == DeliveryGroupStatus.Delivered, cancellationToken);
        var failedStops = await (
            from stop in db.TripStops
            join g in db.DeliveryGroups on stop.DeliveryGroupId equals g.Id
            where g.CustomerId == customer.Id &&
                (stop.Outcome == StopOutcome.Refused || stop.Outcome == StopOutcome.NotHome)
            select stop.Id)
            .CountAsync(cancellationToken);

        // Across every shop: the merchant filter would hide the other shops' refusals from a merchant's request
        var refusedOrders = await db.Orders
            .IgnoreQueryFilters([QueryFilters.Merchant])
            .CountAsync(
                order => order.CustomerId == customer.Id &&
                    (order.Status == OrderStatus.Refused || order.Status == OrderStatus.ReturnedToMerchant),
                cancellationToken);

        return new CustomerStanding(accepted, failedStops + refusedOrders);
    }
}
