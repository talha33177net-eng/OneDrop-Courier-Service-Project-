using Application.Network.ListAreas;
using Application.Parcels.CreateParcel;

namespace Simulator;

/// <summary>Made-up Bangladeshi mobile numbers: 01, an operator digit 3–9, eight digits.</summary>
public static class Phones
{
    public static string Next(Random random)
    {
        return $"01{random.Next(3, 10)}{random.Next(0, 100_000_000):D8}";
    }
}

/// <summary>A parcel to book and the shop that books it.</summary>
public sealed record SimulatedParcel(SimulatedShop Shop, CreateParcelCommand Command);

/// <summary>
/// Makes the parcels of a run: made-up recipients all over the coverage map, most paying cash on delivery, some having
/// paid online, with the weight of what each shop sells. Seeded, so the same seed makes the same parcels again.
/// </summary>
public sealed class ParcelGenerator(IReadOnlyList<SimulatedShop> shops, IReadOnlyList<AreaItem> areas, Random random)
{
    private static readonly string[] Names =
    [
        "Ayesha Siddiqua", "Rahim Uddin", "Nusrat Jahan", "Tanvir Hasan", "Farhana Akter", "Sakib Chowdhury",
        "Mim Rahman", "Arif Hossain", "Sumaiya Islam", "Imran Kabir", "Jannatul Ferdous", "Rakib Ahmed",
        "Tasnim Haque", "Mehedi Hasan", "Sadia Afrin", "Shahriar Alam", "Lamia Karim", "Nayeem Sarker",
        "Riya Das", "Fahim Mahmud", "Anika Tabassum", "Zahid Hossain", "Rumana Begum", "Kawsar Mia"
    ];

    public SimulatedParcel Next()
    {
        var shop = shops[random.Next(shops.Count)];
        var kind = shop.Kind;

        // Most parcels stay in the shop's own city, as for a real Dhaka shop
        var area = random.NextDouble() < 0.7 && areas.Any(a => a.City == shop.City)
            ? areas.Where(a => a.City == shop.City).ElementAt(random.Next(areas.Count(a => a.City == shop.City)))
            : areas[random.Next(areas.Count)];
        var grams = random.Next(kind.MinGrams, kind.MaxGrams + 1);

        return new SimulatedParcel(shop, new CreateParcelCommand
        {
            MerchantReference = $"SIM-{random.Next(100_000, 1_000_000)}",
            RecipientName = Names[random.Next(Names.Length)],
            RecipientPhone = Phones.Next(random),
            RecipientAddress = $"House {random.Next(1, 120)}, Road {random.Next(1, 30)}",
            AreaId = area.Id,
            CodAmount = random.NextDouble() < 0.15 ? 0 : random.Next(30, 301) * 10,
            WeightKg = Math.Round(grams / 1000m, 2),
            ItemDescription = kind.Goods,
            Note = random.NextDouble() < 0.2 ? "Call before coming" : null,
            IdempotencyKey = $"sim-{Guid.NewGuid():N}"
        });
    }
}
