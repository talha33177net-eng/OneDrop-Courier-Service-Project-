using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Domain.Customers;
using Domain.Delivery;
using Domain.Grouping;
using Domain.Merchants;
using Domain.Network;
using Domain.Notifications;
using Domain.Orders;
using Domain.Platform;

namespace Application.Abstractions;

/// <summary>
/// The database as the use cases see it. Every set except <see cref="Tenants"/> is already filtered to the
/// current tenant, and the merchant-owned sets to the current merchant when the caller is one.
/// </summary>
public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }

    DbSet<Hub> Hubs { get; }

    DbSet<Zone> Zones { get; }

    DbSet<Area> Areas { get; }

    DbSet<PickupRoute> PickupRoutes { get; }

    DbSet<Customer> Customers { get; }

    DbSet<CustomerAddress> CustomerAddresses { get; }

    DbSet<PhoneOtp> PhoneOtps { get; }

    DbSet<Merchant> Merchants { get; }

    DbSet<MerchantApiKey> MerchantApiKeys { get; }

    DbSet<PickupPoint> PickupPoints { get; }

    DbSet<Order> Orders { get; }

    DbSet<Package> Packages { get; }

    DbSet<OrderStatusHistory> OrderStatusHistory { get; }

    DbSet<DeliveryGroup> DeliveryGroups { get; }

    DbSet<OutboxMessage> OutboxMessages { get; }

    DbSet<Rider> Riders { get; }

    DbSet<Trip> Trips { get; }

    DbSet<TripStop> TripStops { get; }

    EntityEntry<TEntity> Entry<TEntity>(TEntity entity)
        where TEntity : class;

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
