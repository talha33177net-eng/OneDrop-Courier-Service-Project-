using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Application.Abstractions;
using Domain.Common;
using Domain.Customers;
using Domain.Grouping;
using Domain.Merchants;
using Domain.Network;
using Domain.Orders;
using Domain.Platform;
using Infrastructure.Identity;

namespace Infrastructure.Persistence;

/// <summary>
/// The one DbContext. It maps onto the schema published from src/Database and never creates or
/// migrates anything itself.
///
/// Isolation layer 1 lives here: every <see cref="ITenantOwned"/> entity gets the named filter
/// <see cref="TenantFilter"/> and every <see cref="IMerchantOwned"/> entity the named filter
/// <see cref="MerchantFilter"/>. The filters read <see cref="CurrentTenantId"/> and
/// <see cref="CurrentMerchantId"/> when each query runs, so one cached model serves every tenant. Code that
/// genuinely needs to see across tenants (the API key lookup, the platform portal) must say so with
/// IgnoreQueryFilters([TenantFilter]).
/// </summary>
public class AppDbContext(
    DbContextOptions<AppDbContext> options,
    ITenantContext tenantContext,
    ICurrentUser currentUser)
    : IdentityDbContext<AppUser, AppRole, long>(options), IAppDbContext
{
    public const string TenantFilter = QueryFilters.Tenant;
    public const string MerchantFilter = QueryFilters.Merchant;

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Hub> Hubs => Set<Hub>();

    public DbSet<Zone> Zones => Set<Zone>();

    public DbSet<Area> Areas => Set<Area>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<CustomerAddress> CustomerAddresses => Set<CustomerAddress>();

    public DbSet<PhoneOtp> PhoneOtps => Set<PhoneOtp>();

    public DbSet<Merchant> Merchants => Set<Merchant>();

    public DbSet<MerchantApiKey> MerchantApiKeys => Set<MerchantApiKey>();

    public DbSet<PickupPoint> PickupPoints => Set<PickupPoint>();

    public DbSet<Order> Orders => Set<Order>();

    public DbSet<Package> Packages => Set<Package>();

    public DbSet<OrderStatusHistory> OrderStatusHistory => Set<OrderStatusHistory>();

    public DbSet<DeliveryGroup> DeliveryGroups => Set<DeliveryGroup>();

    /// <summary>Read by the tenant filter at query time. Null means "no tenant", which matches no row.</summary>
    public long? CurrentTenantId => tenantContext.TenantId;

    /// <summary>Read by the merchant filter at query time. Null means the caller is not a merchant.</summary>
    public long? CurrentMerchantId => currentUser.MerchantId;

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // Every DateTime is stored as UTC; this stamps the Kind back on when it is read
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<string>().AreUnicode();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        IdentityMapping.Configure(builder);
        ApplyIsolationFilters(builder);
    }

    private void ApplyIsolationFilters(ModelBuilder builder)
    {
        var context = Expression.Constant(this);
        var tenantId = Expression.Property(context, nameof(CurrentTenantId));
        var merchantId = Expression.Property(context, nameof(CurrentMerchantId));

        foreach (var entityType in builder.Model.GetEntityTypes().Where(t => t.BaseType is null))
        {
            var clrType = entityType.ClrType;
            var row = Expression.Parameter(clrType, "row");

            if (typeof(ITenantOwned).IsAssignableFrom(clrType))
            {
                // row => (long?)row.TenantId == this.CurrentTenantId
                var rowTenant = Expression.Convert(Expression.Property(row, nameof(ITenantOwned.TenantId)), typeof(long?));
                builder.Entity(clrType).HasQueryFilter(
                    TenantFilter,
                    Expression.Lambda(Expression.Equal(rowTenant, tenantId), row));
            }

            var merchantKey = typeof(IMerchantOwned).IsAssignableFrom(clrType)
                ? nameof(IMerchantOwned.MerchantId)
                : clrType == typeof(Merchant) ? nameof(Merchant.Id) : null;
            if (merchantKey is not null)
            {
                // row => this.CurrentMerchantId == null || (long?)row.MerchantId == this.CurrentMerchantId
                var rowMerchant = Expression.Convert(Expression.Property(row, merchantKey), typeof(long?));
                var notAMerchant = Expression.Equal(merchantId, Expression.Constant(null, typeof(long?)));
                builder.Entity(clrType).HasQueryFilter(
                    MerchantFilter,
                    Expression.Lambda(Expression.OrElse(notAMerchant, Expression.Equal(rowMerchant, merchantId)), row));
            }
        }

        // Users are tenant-scoped too, but platform staff have no tenant: null matches null here
        builder.Entity<AppUser>().HasQueryFilter(TenantFilter, user => user.TenantId == CurrentTenantId);
    }
}
