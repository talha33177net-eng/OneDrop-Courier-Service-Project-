using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Customers;
using Domain.Grouping;
using Domain.Network;

namespace Infrastructure.Persistence.Configurations;

public class DeliveryGroupConfiguration : IEntityTypeConfiguration<DeliveryGroup>
{
    public void Configure(EntityTypeBuilder<DeliveryGroup> builder)
    {
        builder.MapTenantOwned(Schemas.Grouping);

        // DG-100001, from the Grouping.DeliveryGroupNumber sequence in the column default
        builder.Property(g => g.Number)
            .HasMaxLength(20)
            .ValueGeneratedOnAdd()
            .HasDefaultValueSql("(concat(N'DG-',NEXT VALUE FOR [Grouping].[DeliveryGroupNumber]))");
        builder.Property(g => g.RowVersion).IsRowVersion();

        builder.HasOne<Customer>().WithMany().HasForeignKey(g => g.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<CustomerAddress>().WithMany().HasForeignKey(g => g.AddressId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Hub>().WithMany().HasForeignKey(g => g.HubId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(g => g.Number).IsUnique().HasDatabaseName("UX_DeliveryGroup_Number");
        builder.HasIndex(g => new { g.CustomerId, g.AddressId })
            .IsUnique()
            .HasFilter("[Status] = 1")
            .HasDatabaseName("UX_DeliveryGroup_Customer_Address_Open");
    }
}
