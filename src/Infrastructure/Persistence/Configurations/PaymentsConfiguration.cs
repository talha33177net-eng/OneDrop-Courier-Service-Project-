using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Merchants;
using Domain.Parcels;
using Domain.Payments;

namespace Infrastructure.Persistence.Configurations;

public class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.MapTenantOwned(Schemas.Payments);
        builder.Property(e => e.Amount).IsMoney();
        builder.Property(e => e.RowVersion).IsRowVersion();
        builder.Ignore(e => e.IsPaidOut);
        builder.HasOne<Merchant>().WithMany().HasForeignKey(e => e.MerchantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Parcel>().WithMany().HasForeignKey(e => e.ParcelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(e => e.Payout).WithMany().HasForeignKey(e => e.PayoutId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.ParcelId, e.Kind }).IsUnique().HasDatabaseName("UX_LedgerEntry_Parcel_Kind");
    }
}

public class PayoutConfiguration : IEntityTypeConfiguration<Payout>
{
    public void Configure(EntityTypeBuilder<Payout> builder)
    {
        builder.MapTenantOwned(Schemas.Payments);

        // INV-100001, from the Payments.PayoutNumber sequence in the column default; EF reads it back after insert
        builder.Property(p => p.Number)
            .HasMaxLength(20)
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("(concat(N'INV-',NEXT VALUE FOR [Payments].[PayoutNumber]))");
        builder.Property(p => p.CodTotal).IsMoney();
        builder.Property(p => p.ChargesTotal).IsMoney();
        builder.Property(p => p.Amount).IsMoney();
        builder.Property(p => p.Account).HasMaxLength(30);
        builder.Property(p => p.GatewayReference).HasMaxLength(100);
        builder.Property(p => p.RowVersion).IsRowVersion();
        builder.HasOne<Merchant>().WithMany().HasForeignKey(p => p.MerchantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => p.Number).IsUnique().HasDatabaseName("UX_Payout_Number");
    }
}
