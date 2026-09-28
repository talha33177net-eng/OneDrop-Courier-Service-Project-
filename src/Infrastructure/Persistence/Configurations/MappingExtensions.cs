using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Domain.Common;
using Domain.Platform;
using Infrastructure.Identity;

namespace Infrastructure.Persistence.Configurations;

/// <summary>SQL schema names. One schema per module, matching the folders of src/Database.</summary>
public static class Schemas
{
    public const string Platform = "Platform";
    public const string Identity = "Identity";
    public const string Network = "Network";
    public const string Customers = "Customers";
    public const string Merchants = "Merchants";
    public const string Orders = "Orders";
    public const string Grouping = "Grouping";
}

internal static class MappingExtensions
{
    /// <summary>Table name = class name, as in the database project. Maps the housekeeping columns.</summary>
    public static EntityTypeBuilder<T> MapAudited<T>(this EntityTypeBuilder<T> builder, string schema)
        where T : AuditedEntity
    {
        builder.ToTable(typeof(T).Name, schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Created).HasColumnType("datetime2(0)");
        builder.Property(e => e.UpdatedOn).HasColumnType("datetime2(7)");
        builder.HasOne<AppUser>().WithMany().HasForeignKey(e => e.UpdatedId).OnDelete(DeleteBehavior.Restrict);

        return builder;
    }

    public static EntityTypeBuilder<T> MapTenantOwned<T>(this EntityTypeBuilder<T> builder, string schema)
        where T : TenantEntity
    {
        builder.MapAudited(schema);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);

        return builder;
    }

    public static PropertyBuilder<decimal> IsMoney(this PropertyBuilder<decimal> builder)
    {
        return builder.HasPrecision(12, 2);
    }
}
