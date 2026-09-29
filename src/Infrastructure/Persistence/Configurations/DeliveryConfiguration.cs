using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Delivery;
using Domain.Grouping;
using Domain.Network;
using Infrastructure.Identity;

namespace Infrastructure.Persistence.Configurations;

public class RiderConfiguration : IEntityTypeConfiguration<Rider>
{
    public void Configure(EntityTypeBuilder<Rider> builder)
    {
        builder.MapTenantOwned(Schemas.Delivery);
        builder.Property(r => r.Name).HasMaxLength(200);
        builder.Property(r => r.Phone).HasMaxLength(20);
        builder.Ignore(r => r.Limit);
        builder.HasOne<Hub>().WithMany().HasForeignKey(r => r.HubId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(r => r.UserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(r => r.UserId)
            .IsUnique()
            .HasFilter("[UserId] IS NOT NULL")
            .HasDatabaseName("UX_Rider_User");
    }
}

public class TripConfiguration : IEntityTypeConfiguration<Trip>
{
    public void Configure(EntityTypeBuilder<Trip> builder)
    {
        builder.MapTenantOwned(Schemas.Delivery);
        builder.Property(t => t.CashExpected).HasPrecision(12, 2);
        builder.Property(t => t.CashReceived).HasPrecision(12, 2);
        builder.Ignore(t => t.CashShort);
        builder.Property(t => t.RowVersion).IsRowVersion();
        builder.HasOne<Rider>().WithMany().HasForeignKey(t => t.RiderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Hub>().WithMany().HasForeignKey(t => t.HubId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(t => new { t.RiderId, t.DeliveryDate })
            .IsUnique()
            .HasDatabaseName("UX_Trip_Rider_DeliveryDate");
    }
}

public class TripStopConfiguration : IEntityTypeConfiguration<TripStop>
{
    public void Configure(EntityTypeBuilder<TripStop> builder)
    {
        builder.MapTenantOwned(Schemas.Delivery);
        builder.Property(s => s.FeeCollected).HasPrecision(10, 2);
        builder.Property(s => s.CodCollected).HasPrecision(12, 2);
        builder.HasOne<Trip>().WithMany().HasForeignKey(s => s.TripId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DeliveryGroup>().WithMany().HasForeignKey(s => s.DeliveryGroupId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(s => s.Payment).WithMany().HasForeignKey(s => s.PaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.DeliveryGroupId, s.DeliveryDate })
            .IsUnique()
            .HasDatabaseName("UX_TripStop_DeliveryGroup_DeliveryDate");
    }
}
