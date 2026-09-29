using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Customers;
using Domain.Delivery;
using Domain.Grouping;
using Domain.Merchants;
using Domain.Orders;
using Domain.Payments;

namespace Infrastructure.Persistence.Configurations;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.MapTenantOwned(Schemas.Payments);
        builder.Property(p => p.Fee).HasPrecision(10, 2);
        builder.Property(p => p.Cod).IsMoney();
        builder.Property(p => p.GatewayReference).HasMaxLength(100);
        builder.Property(p => p.PaymentLink).HasMaxLength(500);
        builder.Property(p => p.RowVersion).IsRowVersion();
        builder.Ignore(p => p.Amount);
        builder.HasOne<Customer>().WithMany().HasForeignKey(p => p.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Trip>().WithMany().HasForeignKey(p => p.TripId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Rider>().WithMany().HasForeignKey(p => p.RiderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeliveryGroup>().WithMany().HasForeignKey(p => p.DeliveryGroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.TenantId, p.CustomerId }).HasDatabaseName("IX_Payment_Tenant_Customer");
        builder.HasIndex(p => new { p.TripId, p.DeliveryGroupId }).HasDatabaseName("IX_Payment_Trip_DeliveryGroup");
    }
}

public class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.MapTenantOwned(Schemas.Payments);
        builder.Property(e => e.Amount).IsMoney();
        builder.Property(e => e.RowVersion).IsRowVersion();
        builder.Ignore(e => e.IsSettled);
        builder.HasOne<Merchant>().WithMany().HasForeignKey(e => e.MerchantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Order>().WithMany().HasForeignKey(e => e.OrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Payment).WithMany().HasForeignKey(e => e.PaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Settlement).WithMany().HasForeignKey(e => e.SettlementId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.OrderId, e.Kind }).IsUnique().HasDatabaseName("UX_LedgerEntry_Order_Kind");
        builder.HasIndex(e => new { e.TenantId, e.MerchantId, e.SettlementId })
            .HasDatabaseName("IX_LedgerEntry_Tenant_Merchant_Settlement");
        builder.HasIndex(e => e.PaymentId).HasDatabaseName("IX_LedgerEntry_Payment");
        builder.HasIndex(e => e.SettlementId).HasDatabaseName("IX_LedgerEntry_Settlement");
    }
}

public class SettlementConfiguration : IEntityTypeConfiguration<Settlement>
{
    public void Configure(EntityTypeBuilder<Settlement> builder)
    {
        builder.MapTenantOwned(Schemas.Payments);
        builder.Property(s => s.Amount).IsMoney();
        builder.Property(s => s.Account).HasMaxLength(20);
        builder.Property(s => s.GatewayReference).HasMaxLength(100);
        builder.Property(s => s.RowVersion).IsRowVersion();
        builder.HasOne<Merchant>().WithMany().HasForeignKey(s => s.MerchantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.TenantId, s.Status }).HasDatabaseName("IX_Settlement_Tenant_Status");
        builder.HasIndex(s => new { s.MerchantId, s.UpToDate }).HasDatabaseName("IX_Settlement_Merchant_UpToDate");
    }
}
