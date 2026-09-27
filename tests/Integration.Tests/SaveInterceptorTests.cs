using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Domain.Customers;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;

namespace Integration.Tests;

/// <summary>Isolation layer 2: even code that bypasses the query filters cannot write to another tenant's rows.</summary>
public class SaveInterceptorTests(WebAppFactory factory)
{
    [Fact]
    public async Task Changing_another_tenants_row_is_refused()
    {
        WebAppFactory.RequireDatabase();
        await using var scope = await ScopeForAsync("chattogram");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dhaka = await TenantIdAsync("dhaka");

        var dhakaHub = await db.Hubs
            .IgnoreQueryFilters([AppDbContext.TenantFilter])
            .FirstAsync(h => h.TenantId == dhaka);
        db.Entry(dhakaHub).Property(h => h.Name).CurrentValue = "Hijacked";

        var refused = await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        Assert.Contains("Refused to write Hub", refused.Message);
    }

    [Fact]
    public async Task A_new_row_takes_the_current_tenant()
    {
        WebAppFactory.RequireDatabase();
        await using var scope = await ScopeForAsync("chattogram");
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var phone = PhoneNumber.Parse("019" + Random.Shared.Next(0, 100_000_000).ToString("D8")).Value;

        var customer = new Customer(phone, "Interceptor test");
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        Assert.Equal(await TenantIdAsync("chattogram"), customer.TenantId);
        Assert.NotEqual(default, customer.Created);
    }

    [Fact]
    public async Task Saving_without_a_tenant_is_refused()
    {
        WebAppFactory.RequireDatabase();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        db.Customers.Add(new Customer(PhoneNumber.Parse("01900000000").Value, null));

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Without_a_tenant_the_query_filter_returns_nothing()
    {
        WebAppFactory.RequireDatabase();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        Assert.Equal(0, await db.Hubs.CountAsync());
        Assert.True(await db.Hubs.IgnoreQueryFilters([AppDbContext.TenantFilter]).AnyAsync());
    }

    private async Task<AsyncServiceScope> ScopeForAsync(string slug)
    {
        var tenant = await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync(slug);
        var scope = factory.Services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant!);

        return scope;
    }

    private async Task<long> TenantIdAsync(string slug)
    {
        var tenant = await factory.Services.GetRequiredService<ITenantCatalog>().FindBySlugAsync(slug);

        return tenant!.Id;
    }
}
