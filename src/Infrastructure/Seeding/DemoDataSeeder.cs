using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Application.Abstractions;
using Application.Common;
using Domain.Merchants;
using Infrastructure.Identity;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;

namespace Infrastructure.Seeding;

/// <summary>
/// Development-only demo data: logins for every role and three merchants per tenant with fixed, documented
/// API keys. Tenants, zones, areas and hubs are NOT created here - they come from the DbUp launch script -
/// because this needs Identity to hash passwords, which SQL cannot do. Safe to run on every start: each
/// row is created only when missing.
/// </summary>
public class DemoDataSeeder(IServiceScopeFactory scopeFactory, ITenantCatalog catalog, ILogger<DemoDataSeeder> logger)
{
    private static readonly DemoMerchant[] DhakaMerchants =
    [
        new("fashion", "Fashion House", "Mirpur 10", "01711000001", "od_dhkfashion01_DevOnlyKeyDoNotUseInProduction01"),
        new("gadget", "Gadget BD", "Mirpur 2", "01711000002", "od_dhkgadget001_DevOnlyKeyDoNotUseInProduction02"),
        new("beauty", "Beauty Shop", "Pallabi", "01711000003", "od_dhkbeauty001_DevOnlyKeyDoNotUseInProduction03")
    ];

    private static readonly DemoMerchant[] ChattogramMerchants =
    [
        new("fashion", "Fashion House", "Agrabad", "01811000001", "od_ctgfashion01_DevOnlyKeyDoNotUseInProduction04"),
        new("gadget", "Gadget BD", "GEC Circle", "01811000002", "od_ctggadget001_DevOnlyKeyDoNotUseInProduction05"),
        new("beauty", "Beauty Shop", "Chawkbazar", "01811000003", "od_ctgbeauty001_DevOnlyKeyDoNotUseInProduction06")
    ];

    public async Task SeedAsync(string password, CancellationToken cancellationToken = default)
    {
        await using (var platformScope = scopeFactory.CreateAsyncScope())
        {
            await EnsureUserAsync(platformScope, "admin@onedrop.test", "OneDrop platform admin", Roles.PlatformAdmin, password, null, null);
        }

        foreach (var (slug, merchants) in new[] { ("dhaka", DhakaMerchants), ("chattogram", ChattogramMerchants) })
        {
            var tenant = await catalog.FindBySlugAsync(slug, cancellationToken);
            if (tenant is null)
            {
                logger.LogWarning("Demo seed skipped {Slug}: run dbup first, it creates the launch tenants", slug);
                continue;
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant);

            await EnsureUserAsync(scope, $"admin@{slug}.onedrop.test", $"{tenant.Name} admin", Roles.TenantAdmin, password, tenant.Id, null);
            await EnsureUserAsync(scope, $"hub@{slug}.onedrop.test", $"{tenant.Name} hub staff", Roles.HubStaff, password, tenant.Id, null);

            foreach (var demo in merchants)
            {
                var merchantId = await EnsureMerchantAsync(scope, demo, cancellationToken);
                if (merchantId.HasValue)
                {
                    await EnsureUserAsync(scope, $"{demo.Key}@{slug}.onedrop.test", demo.Name, Roles.Merchant, password, tenant.Id, merchantId);
                }
            }
        }

        logger.LogInformation("Demo data ready. Logins and API keys are listed in README.md");
    }

    private async Task<long?> EnsureMerchantAsync(AsyncServiceScope scope, DemoMerchant demo, CancellationToken cancellationToken)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var existing = await db.Merchants.FirstOrDefaultAsync(m => m.Name == demo.Name, cancellationToken);
        if (existing is not null)
        {
            return existing.Id;
        }

        var area = await db.Areas.FirstOrDefaultAsync(a => a.Name == demo.Area, cancellationToken);
        if (area is null)
        {
            logger.LogWarning("Demo merchant {Merchant} skipped: area {Area} does not exist", demo.Name, demo.Area);
            return null;
        }

        var merchant = new Merchant(demo.Name, area.ZoneId, demo.Phone, $"{demo.Key}@example.com");
        db.Merchants.Add(merchant);
        await db.SaveChangesAsync(cancellationToken);

        db.PickupPoints.Add(new PickupPoint(merchant.Id, area.Id, "Shop", $"{demo.Name}, {area.Name}", demo.Phone, isDefault: true));
        db.MerchantApiKeys.Add(MerchantApiKey.FromPlaintext(merchant.Id, "Demo website", demo.ApiKey).Value);
        await db.SaveChangesAsync(cancellationToken);

        return merchant.Id;
    }

    private async Task EnsureUserAsync(
        AsyncServiceScope scope,
        string email,
        string displayName,
        string role,
        string password,
        long? tenantId,
        long? merchantId)
    {
        var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        if (await users.FindByNameAsync(email) is not null)
        {
            return;
        }

        var user = new AppUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = displayName,
            TenantId = tenantId,
            MerchantId = merchantId,
            Created = DateTime.UtcNow
        };
        var created = await users.CreateAsync(user, password);
        if (created.Succeeded)
        {
            created = await users.AddToRoleAsync(user, role);
        }

        if (!created.Succeeded)
        {
            throw new InvalidOperationException(
                $"Could not seed {email}: {string.Join("; ", created.Errors.Select(e => e.Description))}");
        }
    }

    private sealed record DemoMerchant(string Key, string Name, string Area, string Phone, string ApiKey);
}
