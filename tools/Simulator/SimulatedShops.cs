using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Application.Abstractions;
using Domain.Merchants;
using Infrastructure.MultiTenancy;
using Infrastructure.Persistence;

namespace Simulator;

/// <summary>What a made-up shop sells: the item description and its weight range.</summary>
public sealed record ShopKind(string Key, string Name, string Goods, int MinGrams, int MaxGrams);

/// <summary>A simulated shop of one courier, the city it picks up in, and the API key issued for this run.</summary>
public sealed record SimulatedShop(long Id, ShopKind Kind, string City, string ApiKey);

/// <summary>
/// Makes the simulator's shops once per courier (found again by their contact address), active and with a bKash payout
/// account, and issues each a new API key for the run, revoking the one from the run before: the plaintext is never
/// stored, so a key cannot be reused.
/// </summary>
public static class SimulatedShops
{
    public const string KeyName = "Simulator";

    public static readonly ShopKind[] Kinds =
    [
        new("nakshi", "Nakshi Crafts", "Nakshi kantha", 400, 1_500),
        new("boighor", "Boi Ghor", "Books", 300, 2_000),
        new("gadget", "Gadget Corner", "Phone accessories", 150, 800),
        new("threads", "Deshi Threads", "Panjabi", 300, 900),
        new("rupsha", "Rupsha Cosmetics", "Skin care", 100, 500),
        new("shishu", "Shishu Toys", "Toys", 300, 2_500),
        new("krishi", "Krishi Fresh", "Mangoes", 2_000, 5_000),
        new("kitchen", "Home Kitchen", "Kitchenware", 800, 4_000)
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
        var areas = await db.Areas.Include(a => a.Zone).OrderBy(a => a.Id).ToListAsync(cancellationToken);
        var shops = new List<SimulatedShop>();

        foreach (var kind in Kinds.Take(count))
        {
            var email = EmailOf(kind);
            var merchant = await db.Merchants.FirstOrDefaultAsync(m => m.ContactEmail == email, cancellationToken);
            if (merchant is null)
            {
                var area = areas[random.Next(areas.Count)];
                var phone = Phones.Next(random);
                merchant = Merchant.Add(new MerchantProfile(kind.Name, "Simulated owner", phone, email, $"{kind.Name}, {area.Name}")).Value;
                merchant.SetPayoutAccount(PayoutMethod.Bkash, phone, "Simulated owner");
                db.Merchants.Add(merchant);
                await db.SaveChangesAsync(cancellationToken);
                db.PickupPoints.Add(PickupPoint.Create(merchant.Id, area.Id, "Shop", $"{kind.Name}, {area.Name}", phone, isDefault: true).Value);
            }

            var now = time.GetUtcNow().UtcDateTime;
            await db.MerchantApiKeys
                .Where(k => k.MerchantId == merchant.Id && k.Name == KeyName && k.RevokedOn == null)
                .ForEachAsync(k => k.Revoke(now), cancellationToken);
            var (key, plaintext) = MerchantApiKey.Issue(merchant.Id, KeyName);
            db.MerchantApiKeys.Add(key);
            await db.SaveChangesAsync(cancellationToken);

            var merchantId = merchant.Id;
            var city = await db.PickupPoints
                .Where(p => p.MerchantId == merchantId && p.IsDefault)
                .Join(db.Areas, p => p.AreaId, a => a.Id, (p, a) => a.Zone!.City)
                .FirstAsync(cancellationToken);
            shops.Add(new SimulatedShop(merchant.Id, kind, city, plaintext));
        }

        return shops;
    }
}
