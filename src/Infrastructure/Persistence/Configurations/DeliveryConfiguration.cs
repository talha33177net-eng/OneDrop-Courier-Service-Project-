using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Delivery;
using Domain.Merchants;
using Domain.Network;
using Domain.Parcels;
using Infrastructure.Identity;

namespace Infrastructure.Persistence.Configurations;

public class RiderConfiguration : IEntityTypeConfiguration<Rider>
{
    public void Configure(EntityTypeBuilder<Rider> builder)
    {
        builder.MapTenantOwned(Schemas.Delivery);
        builder.Property(r => r.Name).HasMaxLength(200);
        builder.Property(r => r.Phone).HasMaxLength(20);
        builder.HasOne<Hub>().WithMany().HasForeignKey(r => r.HubId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.UserId)
            .IsUnique()
            .HasFilter("[UserId] IS NOT NULL")
            .HasDatabaseName("UX_Rider_User");
    }
}

public class VehicleCapacityConfiguration : IEntityTypeConfiguration<VehicleCapacity>
{
    public void Configure(EntityTypeBuilder<VehicleCapacity> builder)
    {
        builder.MapTenantOwned(Schemas.Delivery);
        builder.HasIndex(c => new { c.TenantId, c.Vehicle }).IsUnique().HasDatabaseName("UX_VehicleCapacity_Tenant_Vehicle");
    }
}

public class DeliveryRunConfiguration : IEntityTypeConfiguration<DeliveryRun>
{
    public void Configure(EntityTypeBuilder<DeliveryRun> builder)
    {
        builder.MapTenantOwned(Schemas.Delivery);
        builder.Property(r => r.CashExpected).HasPrecision(12, 2);
        builder.Property(r => r.CashReceived).HasPrecision(12, 2);
        builder.Ignore(r => r.CashShort);
        builder.Property(r => r.RowVersion).IsRowVersion();
        builder.HasOne<Rider>().WithMany().HasForeignKey(r => r.RiderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Hub>().WithMany().HasForeignKey(r => r.HubId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => new { r.RiderId, r.RunDate }).IsUnique().HasDatabaseName("UX_DeliveryRun_Rider_RunDate");
    }
}

public class DeliveryAttemptConfiguration : IEntityTypeConfiguration<DeliveryAttempt>
{
    public void Configure(EntityTypeBuilder<DeliveryAttempt> builder)
    {
        builder.MapTenantOwned(Schemas.Delivery);
        builder.Property(a => a.CollectedAmount).IsMoney();
        builder.Property(a => a.Reason).HasMaxLength(200);
        builder.Ignore(a => a.IsDone);
        builder.HasOne<DeliveryRun>().WithMany().HasForeignKey(a => a.RunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Rider>().WithMany().HasForeignKey(a => a.RiderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Parcel>().WithMany().HasForeignKey(a => a.ParcelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Merchant>().WithMany().HasForeignKey(a => a.MerchantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => a.ParcelId)
            .IsUnique()
            .HasFilter("[Outcome] IS NULL")
            .HasDatabaseName("UX_DeliveryAttempt_Parcel_Open");
    }
}

public class PickupRequestConfiguration : IEntityTypeConfiguration<PickupRequest>
{
    public void Configure(EntityTypeBuilder<PickupRequest> builder)
    {
        builder.MapTenantOwned(Schemas.Delivery);
        builder.Property(r => r.Note).HasMaxLength(300);
        builder.Property(r => r.RowVersion).IsRowVersion();
        builder.Ignore(r => r.IsOpen);
        builder.HasOne<Merchant>().WithMany().HasForeignKey(r => r.MerchantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PickupPoint>().WithMany().HasForeignKey(r => r.PickupPointId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Hub>().WithMany().HasForeignKey(r => r.HubId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Rider>().WithMany().HasForeignKey(r => r.RiderId).OnDelete(DeleteBehavior.Restrict);
    }
}
