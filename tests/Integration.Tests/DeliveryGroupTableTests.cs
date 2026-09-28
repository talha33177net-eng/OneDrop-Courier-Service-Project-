using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Domain.Customers;
using Domain.Grouping;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>
/// The rule "one open group per customer and address, even with two orders at the same moment" rests on the
/// filtered unique index UX_DeliveryGroup_Customer_Address_Open; the grouping code relies on it to settle races.
/// </summary>
public class DeliveryGroupTableTests(WebAppFactory factory)
{
    private static readonly TimeZoneInfo Dhaka = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dhaka");

    [Fact]
    public async Task A_new_group_gets_a_number_and_the_current_tenant()
    {
        WebAppFactory.RequireDatabase();
        await using var scope = await ScopeForAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var address = await NewAddressAsync(db);

        var group = await OpenAsync(db, address);

        Assert.StartsWith("DG-", group.Number);
        Assert.Equal(scope.ServiceProvider.GetRequiredService<ITenantContext>().TenantId, group.TenantId);
        Assert.NotEmpty(group.RowVersion);
    }

    [Fact]
    public async Task A_second_open_group_for_the_same_customer_and_address_is_refused()
    {
        WebAppFactory.RequireDatabase();
        await using var scope = await ScopeForAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var address = await NewAddressAsync(db);
        await OpenAsync(db, address);

        var exception = await Assert.ThrowsAsync<DbUpdateException>(() => OpenAsync(db, address));

        Assert.Contains("UX_DeliveryGroup_Customer_Address_Open", exception.InnerException!.Message);
    }

    [Fact]
    public async Task Once_locked_a_new_group_can_open_for_the_same_address()
    {
        WebAppFactory.RequireDatabase();
        await using var scope = await ScopeForAsync("dhaka");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var address = await NewAddressAsync(db);
        var first = await OpenAsync(db, address);
        var cancellation = TestContext.Current.CancellationToken;
        first.MoveTo(DeliveryGroupStatus.Locked, DateTime.UtcNow);
        await db.SaveChangesAsync(cancellation);

        var second = await OpenAsync(db, address);

        Assert.NotEqual(first.Number, second.Number);
        Assert.Equal(1, await db.DeliveryGroups.CountAsync(
            g => g.CustomerId == address.CustomerId && g.Status == DeliveryGroupStatus.Open,
            cancellation));
    }

    private static async Task<DeliveryGroup> OpenAsync(AppDbContext db, CustomerAddress address)
    {
        var hubId = await db.Areas
            .Where(a => a.Id == address.AreaId)
            .Select(a => a.Zone!.HubId)
            .SingleAsync();
        var group = DeliveryGroup.Open(new NewDeliveryGroup(
            address.CustomerId,
            address.Id,
            hubId,
            DateTime.UtcNow,
            Dhaka,
            JoinDays: 2));
        db.DeliveryGroups.Add(group);
        await db.SaveChangesAsync();

        return group;
    }

    private static async Task<CustomerAddress> NewAddressAsync(AppDbContext db)
    {
        var phone = PhoneNumber.Parse("018" + Random.Shared.Next(0, 100_000_000).ToString("D8")).Value;
        var customer = new Customer(phone, "Group test");
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var areaId = await db.Areas.Select(a => a.Id).FirstAsync();
        var address = new CustomerAddress(customer.Id, areaId, "House 1, Road 1", null, null);
        db.CustomerAddresses.Add(address);
        await db.SaveChangesAsync();

        return address;
    }

    private async Task<AsyncServiceScope> ScopeForAsync(string slug)
    {
        var tenant = await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync(slug);
        var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant!);

        return scope;
    }
}
