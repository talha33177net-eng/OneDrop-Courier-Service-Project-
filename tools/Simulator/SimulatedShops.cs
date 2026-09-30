using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Domain.Merchants;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;

namespace Simulator;

/// <summary>What a made-up shop sells: the package description, its weight range and whether it may wait.</summary>
public sealed record ShopKind(string Key, string Name, string Goods, int MinGrams, int MaxGrams, bool DoNotHold);

/// <summary>A simulated shop of one operator, with the API key issued for this run.</summary>
public sealed record SimulatedShop(long Id, ShopKind Kind, string ApiKey);

/// <summary>
/// Makes the simulator's shops once per operator (found again by their contact address) and issues each a new API key
/// for the run, revoking the one from the run before: the plaintext is never stored, so a key cannot be reused.
/// </summary>
public static class SimulatedShops
{
    public const string KeyName = "Simulator";

    public static readonly ShopKind[] Kinds =
    [
        new("nakshi", "Nakshi Crafts", "Nakshi kantha", 400, 1_500, false),
        new("boighor", "Boi Ghor", "Books", 300, 2_000, false),
        new("gadget", "Gadget Corner", "Phone accessories", 150, 800, false),
        new("threads", "Deshi Threads", "Panjabi", 300, 900, false),
        new("rupsha", "Rupsha Cosmetics", "Skin care", 100, 500, false),
        new("shishu", "Shishu Toys", "Toys", 300, 2_500, false),
        new("krishi", "Krishi Fresh", "Mangoes", 2_000, 5_000, true),
        new("kitchen", "Home Kitchen", "Kitchenware", 800, 4_000, false)
    ];

    public static string EmailOf(ShopKind kind)
    {
        return $"{kind.Key}@simulator.example";
    }

    public static async Task<IReadOnlyList<SimulatedShop>> EnsureAsync(
        IServiceProvider services,
        TenantInfo tenant,
        int count,
        Random random,
        TimeProvider time,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<TenantContext>().Set(tenant);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var areas = await db.Areas.OrderBy(a => a.Id).ToListAsync(cancellationToken);
        var shops = new List<SimulatedShop>();

        foreach (var kind in Kinds.Take(count))
        {
            var email = EmailOf(kind);
            var merchant = await db.Merchants.FirstOrDefaultAsync(m => m.ContactEmail == email, cancellationToken);
            if (merchant is null)
            {
                var area = areas[random.Next(areas.Count)];
                merchant = new Merchant(kind.Name, area.ZoneId, Phones.Next(random), email);
                db.Merchants.Add(merchant);
                await db.SaveChangesAsync(cancellationToken);
                db.PickupPoints.Add(new PickupPoint(
                    merchant.Id, area.Id, "Shop", $"{kind.Name}, {area.Name}", merchant.ContactPhone, isDefault: true));
            }

            var now = time.GetUtcNow().UtcDateTime;
            await db.MerchantApiKeys
                .Where(k => k.MerchantId == merchant.Id && k.Name == KeyName && k.RevokedOn == null)
                .ForEachAsync(k => k.Revoke(now), cancellationToken);
            var (key, plaintext) = MerchantApiKey.Issue(merchant.Id, KeyName);
            db.MerchantApiKeys.Add(key);
            await db.SaveChangesAsync(cancellationToken);
            shops.Add(new SimulatedShop(merchant.Id, kind, plaintext));
        }

        return shops;
    }
}
