using Application.Network.ListAreas;
using Application.Orders.CreateOrder;
using Domain.Orders;

namespace Simulator;

/// <summary>Made-up Bangladeshi mobile numbers: 01, an operator digit 3–9, eight digits.</summary>
public static class Phones
{
    public static string Next(Random random)
    {
        return $"01{random.Next(3, 10)}{random.Next(0, 100_000_000):D8}";
    }
}

/// <summary>One made-up customer and the addresses they order to (home, sometimes an office).</summary>
public sealed record SimulatedCustomer(string Name, string Phone, IReadOnlyList<AddressInput> Addresses);

/// <summary>An order to send and the shop that sends it.</summary>
public sealed record SimulatedOrder(SimulatedShop Shop, CreateOrderCommand Command);

/// <summary>
/// Makes the orders of a run: customers who buy from several shops, so that deliveries group the way the business
/// hopes (about two shops a delivery), mostly to their home address, some paid online, a few fast. Seeded, so the
/// same seed makes the same customers again and their orders join the deliveries already open.
/// </summary>
public sealed class OrderGenerator(IReadOnlyList<SimulatedShop> shops, IReadOnlyList<AreaItem> areas, Random random)
{
    private static readonly string[] Names =
    [
        "Ayesha Siddiqua", "Rahim Uddin", "Nusrat Jahan", "Tanvir Hasan", "Farhana Akter", "Sakib Chowdhury",
        "Mim Rahman", "Arif Hossain", "Sumaiya Islam", "Imran Kabir", "Jannatul Ferdous", "Rakib Ahmed",
        "Tasnim Haque", "Mehedi Hasan", "Sadia Afrin", "Shahriar Alam", "Lamia Karim", "Nayeem Sarker",
        "Riya Das", "Fahim Mahmud", "Anika Tabassum", "Zahid Hossain", "Rumana Begum", "Kawsar Mia"
    ];

    private readonly Dictionary<SimulatedCustomer, HashSet<long>> shopsUsed = [];

    /// <summary>Enough customers that each gets about two orders from different shops.</summary>
    public IReadOnlyList<SimulatedCustomer> Customers(int orders)
    {
        return
        [
            .. Enumerable.Range(0, Math.Max(1, (int)Math.Ceiling(orders / 2.2))).Select(_ =>
            {
                var home = Address();
                return new SimulatedCustomer(
                    Names[random.Next(Names.Length)],
                    Phones.Next(random),
                    random.NextDouble() < 0.2 ? [home, Address()] : [home]);
            })
        ];
    }

    public SimulatedOrder Next(IReadOnlyList<SimulatedCustomer> customers)
    {
        var customer = customers[random.Next(customers.Count)];
        var used = shopsUsed.TryGetValue(customer, out var found) ? found : shopsUsed[customer] = [];
        var fresh = shops.Where(s => !used.Contains(s.Id)).ToList();
        var shop = fresh.Count > 0 ? fresh[random.Next(fresh.Count)] : shops[random.Next(shops.Count)];
        used.Add(shop.Id);
        var kind = shop.Kind;
        var packages = random.NextDouble() < 0.2 ? 2 : 1;

        return new SimulatedOrder(shop, new CreateOrderCommand
        {
            ExternalReference = $"SIM-{random.Next(100_000, 1_000_000)}",
            Customer = new CustomerInput(customer.Name, customer.Phone),
            Address = customer.Addresses.Count > 1 && random.NextDouble() < 0.15
                ? customer.Addresses[1]
                : customer.Addresses[0],
            Packages =
            [
                .. Enumerable.Range(0, packages).Select(_ =>
                    new PackageInput(kind.Goods, random.Next(kind.MinGrams, kind.MaxGrams + 1)))
            ],
            CodAmount = random.NextDouble() < 0.15 ? 0 : random.Next(30, 301) * 10,
            Speed = random.NextDouble() < 0.08 ? DeliverySpeed.Fast : DeliverySpeed.Combine,
            DoNotHold = kind.DoNotHold,
            IdempotencyKey = $"sim-{Guid.NewGuid():N}"
        });
    }

    private AddressInput Address()
    {
        var area = areas[random.Next(areas.Count)];
        var landmark = random.NextDouble() < 0.3 ? $"Near {area.Name} mosque" : null;

        return new AddressInput(area.Id, null, $"House {random.Next(1, 120)}, Road {random.Next(1, 30)}", null, landmark);
    }
}
