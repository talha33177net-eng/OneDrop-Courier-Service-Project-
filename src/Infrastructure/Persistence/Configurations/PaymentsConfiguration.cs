using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Customers;
using Domain.Delivery;
using Domain.Grouping;
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
