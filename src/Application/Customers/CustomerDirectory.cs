using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Customers;

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
}
