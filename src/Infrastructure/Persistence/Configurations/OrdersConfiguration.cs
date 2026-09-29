using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Customers;
using Domain.Grouping;
using Domain.Merchants;
using Domain.Network;
using Domain.Orders;

namespace Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.MapTenantOwned(Schemas.Orders);

        // OD-100001, from the Orders.OrderNumber sequence in the column default; EF reads it back after insert
        builder.Property(o => o.Number)
            .HasMaxLength(20)
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("(concat(N'OD-',NEXT VALUE FOR [Orders].[OrderNumber]))");
        builder.Property(o => o.ExternalReference).HasMaxLength(100);
        builder.Property(o => o.IdempotencyKey).HasMaxLength(100);
        builder.Property(o => o.RequestHash).HasMaxLength(32).IsFixedLength();
        builder.Property(o => o.RecipientName).HasMaxLength(200);
        builder.Property(o => o.Note).HasMaxLength(500);
        builder.Property(o => o.CodAmount).IsMoney();
        builder.Property(o => o.DeclaredValue).IsMoney();
        builder.Property(o => o.AddedFee).HasPrecision(10, 2);
        builder.Property(o => o.RowVersion).IsRowVersion();
        builder.Ignore(o => o.TotalWeightGrams);
        builder.Property(o => o.CustomerToken).HasMaxLength(30);
        builder.Ignore(o => o.WaitsForCustomer);
        builder.Ignore(o => o.WaitsForAdvance);

        builder.HasOne<Merchant>().WithMany().HasForeignKey(o => o.MerchantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Customer>().WithMany().HasForeignKey(o => o.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CustomerAddress>().WithMany().HasForeignKey(o => o.AddressId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PickupPoint>().WithMany().HasForeignKey(o => o.PickupPointId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(o => o.DeliveryGroup).WithMany().HasForeignKey(o => o.DeliveryGroupId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(o => o.Packages).WithOne(p => p.Order).HasForeignKey(p => p.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(o => o.Packages).HasField("packages");
        builder.HasMany(o => o.History).WithOne(h => h.Order).HasForeignKey(h => h.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.Navigation(o => o.History).HasField("history");

        builder.HasIndex(o => o.Number).IsUnique().HasDatabaseName("UX_Order_Number");
        builder.HasIndex(o => o.CustomerToken)
            .IsUnique()
            .HasFilter("[CustomerToken] IS NOT NULL")
            .HasDatabaseName("UX_Order_CustomerToken");
        builder.HasIndex(o => new { o.MerchantId, o.IdempotencyKey })
            .IsUnique()
            .HasFilter("[IdempotencyKey] IS NOT NULL")
            .HasDatabaseName("UX_Order_Merchant_IdempotencyKey");
    }
}

public class PackageConfiguration : IEntityTypeConfiguration<Package>
{
    public void Configure(EntityTypeBuilder<Package> builder)
    {
        builder.MapTenantOwned(Schemas.Orders);
        builder.Property(p => p.Description).HasMaxLength(200);
        builder.HasOne<Hub>().WithMany().HasForeignKey(p => p.HubId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Hub>().WithMany().HasForeignKey(p => p.ShuttleToHubId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.OrderId, p.Sequence }).IsUnique().HasDatabaseName("UX_Package_Order_Sequence");
        builder.HasIndex(p => p.HubId).HasFilter("[HubId] IS NOT NULL").HasDatabaseName("IX_Package_Hub");
        builder.HasIndex(p => p.ShuttleToHubId)
            .HasFilter("[ShuttleToHubId] IS NOT NULL")
            .HasDatabaseName("IX_Package_ShuttleToHub");
    }
}

public class OrderStatusHistoryConfiguration : IEntityTypeConfiguration<OrderStatusHistory>
{
    public void Configure(EntityTypeBuilder<OrderStatusHistory> builder)
    {
        builder.MapTenantOwned(Schemas.Orders);
        builder.Property(h => h.Note).HasMaxLength(500);
    }
}
