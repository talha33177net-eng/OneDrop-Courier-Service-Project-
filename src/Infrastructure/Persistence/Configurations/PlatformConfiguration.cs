using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Platform;

namespace Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.MapAudited(Schemas.Platform);
        builder.Property(t => t.Name).HasMaxLength(200);
        builder.Property(t => t.Slug).HasMaxLength(50);
        builder.Property(t => t.TimeZone).HasMaxLength(100);
        builder.Property(t => t.CurrencyCode).HasMaxLength(3).IsFixedLength();
        builder.Property(t => t.SmsSenderName).HasMaxLength(20);
        builder.Property(t => t.BaseDeliveryFee).HasPrecision(10, 2);
        builder.Property(t => t.ExtraShopFee).HasPrecision(10, 2);
        builder.Property(t => t.FastDeliveryFee).HasPrecision(10, 2);
        builder.Property(t => t.ExtraKgFee).HasPrecision(10, 2);
        builder.Property(t => t.ReturnCharge).HasPrecision(10, 2);
        builder.Property(t => t.LateHandoverFee).HasPrecision(10, 2);
        builder.HasIndex(t => t.Slug).IsUnique().HasDatabaseName("UX_Tenant_Slug");
    }
}
