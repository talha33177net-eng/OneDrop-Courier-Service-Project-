using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Customers;
using Domain.Network;

namespace Infrastructure.Persistence.Configurations;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.MapTenantOwned(Schemas.Customers);
        builder.Property(c => c.Phone).HasMaxLength(20);
        builder.Property(c => c.Name).HasMaxLength(200);
        builder.HasIndex(c => new { c.TenantId, c.Phone }).IsUnique().HasDatabaseName("UX_Customer_Tenant_Phone");
    }
}

public class CustomerAddressConfiguration : IEntityTypeConfiguration<CustomerAddress>
{
    public void Configure(EntityTypeBuilder<CustomerAddress> builder)
    {
        builder.MapTenantOwned(Schemas.Customers);
        builder.Property(a => a.Line1).HasMaxLength(300);
        builder.Property(a => a.Line2).HasMaxLength(300);
        builder.Property(a => a.Landmark).HasMaxLength(300);
        builder.Property(a => a.MatchKey).HasMaxLength(CustomerAddress.MatchKeyLength);
        builder.HasOne<Customer>().WithMany().HasForeignKey(a => a.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Area>().WithMany().HasForeignKey(a => a.AreaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CustomerAddress>().WithMany().HasForeignKey(a => a.SameAsId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.CustomerId, a.AreaId, a.MatchKey })
            .IsUnique()
            .HasDatabaseName("UX_CustomerAddress_Customer_Area_MatchKey");
    }
}

public class PhoneOtpConfiguration : IEntityTypeConfiguration<PhoneOtp>
{
    public void Configure(EntityTypeBuilder<PhoneOtp> builder)
    {
        builder.MapTenantOwned(Schemas.Customers);
        builder.Property(o => o.Phone).HasMaxLength(20);
        builder.Property(o => o.CodeHash).HasMaxLength(32).IsFixedLength();
        builder.Property(o => o.ExpiresOn).HasColumnType("datetime2(0)");
        builder.Property(o => o.ConsumedOn).HasColumnType("datetime2(0)");
    }
}
