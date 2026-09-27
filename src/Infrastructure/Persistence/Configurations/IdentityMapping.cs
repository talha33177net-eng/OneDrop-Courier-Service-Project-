using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Infrastructure.Identity;

namespace Infrastructure.Persistence;

/// <summary>
/// Moves the ASP.NET Core Identity tables into the Identity schema with singular names (Identity.User,
/// Identity.Role, ...) and makes user names unique per tenant instead of globally. Runs after
/// IdentityDbContext's own configuration, so it only overrides.
/// </summary>
internal static class IdentityMapping
{
    public static void Configure(ModelBuilder builder)
    {
        const string schema = Configurations.Schemas.Identity;

        builder.Entity<AppUser>(user =>
        {
            user.ToTable("User", schema);
            user.Property(u => u.DisplayName).HasMaxLength(200);
            user.Property(u => u.Created).HasColumnType("datetime2(0)");

            // Identity's global UserNameIndex would stop the same phone signing up in two cities
            user.Metadata.RemoveIndex([user.Metadata.FindProperty(nameof(AppUser.NormalizedUserName))!]);
            // No TenantId filter: SQL Server treats NULLs as equal here, so platform user names stay unique too
            user.HasIndex(u => new { u.TenantId, u.NormalizedUserName })
                .IsUnique()
                .HasFilter("[NormalizedUserName] IS NOT NULL")
                .HasDatabaseName("UX_User_Tenant_UserName");
            user.HasIndex(u => u.NormalizedEmail).HasDatabaseName("IX_User_NormalizedEmail");
        });

        builder.Entity<AppRole>(role =>
        {
            role.ToTable("Role", schema);
            role.HasIndex(r => r.NormalizedName).IsUnique().HasDatabaseName("UX_Role_NormalizedName");
        });

        builder.Entity<IdentityUserRole<long>>().ToTable("UserRole", schema);
        builder.Entity<IdentityUserClaim<long>>().ToTable("UserClaim", schema);
        builder.Entity<IdentityUserLogin<long>>().ToTable("UserLogin", schema);
        builder.Entity<IdentityUserToken<long>>().ToTable("UserToken", schema);
        builder.Entity<IdentityRoleClaim<long>>().ToTable("RoleClaim", schema);
    }
}
