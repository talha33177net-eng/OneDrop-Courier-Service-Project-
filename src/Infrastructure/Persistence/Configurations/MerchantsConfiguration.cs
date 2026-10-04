using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Merchants;
using Domain.Network;

namespace Infrastructure.Persistence.Configurations;

public class MerchantConfiguration : IEntityTypeConfiguration<Merchant>
{
    public void Configure(EntityTypeBuilder<Merchant> builder)
    {
        builder.MapTenantOwned(Schemas.Merchants);
        builder.Property(m => m.Name).HasMaxLength(200);
        builder.Property(m => m.OwnerName).HasMaxLength(200);
        builder.Property(m => m.ContactPhone).HasMaxLength(20);
        builder.Property(m => m.ContactEmail).HasMaxLength(320);
        builder.Property(m => m.Address).HasMaxLength(500);
        builder.Property(m => m.PayoutAccount).HasMaxLength(30);
        builder.Property(m => m.PayoutAccountName).HasMaxLength(200);
        builder.Property(m => m.WebhookUrl).HasMaxLength(Merchant.MaxWebhookUrlLength);
        builder.Property(m => m.WebhookSecret).HasMaxLength(100);
        builder.Ignore(m => m.CanBook);
        builder.Ignore(m => m.HasPayoutAccount);
        builder.HasIndex(m => new { m.TenantId, m.Name }).IsUnique().HasDatabaseName("UX_Merchant_Tenant_Name");
    }
}

public class MerchantApiKeyConfiguration : IEntityTypeConfiguration<MerchantApiKey>
{
    public void Configure(EntityTypeBuilder<MerchantApiKey> builder)
    {
        builder.MapTenantOwned(Schemas.Merchants);
        builder.Property(k => k.Name).HasMaxLength(100);
        builder.Property(k => k.Prefix).HasMaxLength(MerchantApiKey.PrefixLength);
        builder.Property(k => k.KeyHash).HasMaxLength(32).IsFixedLength();
        builder.Property(k => k.LastUsedOn).HasColumnType("datetime2(0)");
        builder.Property(k => k.RevokedOn).HasColumnType("datetime2(0)");
        builder.Ignore(k => k.IsActive);
        builder.HasOne<Merchant>().WithMany().HasForeignKey(k => k.MerchantId).OnDelete(DeleteBehavior.Restrict);

        // Global, not per tenant: the prefix is how a key is found before the tenant is known
        builder.HasIndex(k => k.Prefix).IsUnique().HasDatabaseName("UX_MerchantApiKey_Prefix");
    }
}

public class PickupPointConfiguration : IEntityTypeConfiguration<PickupPoint>
{
    public void Configure(EntityTypeBuilder<PickupPoint> builder)
    {
        builder.MapTenantOwned(Schemas.Merchants);
        builder.Property(p => p.Name).HasMaxLength(200);
        builder.Property(p => p.Address).HasMaxLength(500);
        builder.Property(p => p.ContactPhone).HasMaxLength(20);
        builder.HasOne<Merchant>().WithMany().HasForeignKey(p => p.MerchantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Area>().WithMany().HasForeignKey(p => p.AreaId).OnDelete(DeleteBehavior.Restrict);
    }
}
