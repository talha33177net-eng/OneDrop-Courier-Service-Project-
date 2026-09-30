using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions;
using Application.Network.ListAreas;

namespace Simulator;

/// <param name="Sent">Orders the API accepted.</param>
/// <param name="Joined">Of those, orders that joined a delivery already on its way (they cost the extra-shop fee or nothing).</param>
/// <param name="Refused">Orders the API refused, with its answer.</param>
/// <param name="Numbers">The accepted orders' numbers.</param>
public sealed record SimulationResult(
    string Tenant, int Sent, int Joined, IReadOnlyList<string> Refused, IReadOnlyList<string> Numbers);

/// <summary>One operator's run: make sure its shops exist, then send the orders through the API, one by one.</summary>
public static class Simulation
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static async Task<SimulationResult> RunAsync(
        IServiceProvider services,
        HttpClient api,
        TenantInfo tenant,
        SimulationOptions options,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        var random = options.Seed is { } seed ? new Random(seed + (int)tenant.Id) : new Random();
        var shops = await SimulatedShops.EnsureAsync(
            services, tenant, options.Shops, random, TimeProvider.System, cancellationToken);

        using var areasRequest = new HttpRequestMessage(HttpMethod.Get, "api/v1/areas");
        areasRequest.Headers.Add("X-Api-Key", shops[0].ApiKey);
        using var areasResponse = await api.SendAsync(areasRequest, cancellationToken);
        areasResponse.EnsureSuccessStatusCode();
        var areas = await areasResponse.Content.ReadFromJsonAsync<List<AreaItem>>(Json, cancellationToken)
            ?? throw new InvalidOperationException("The API returned no areas.");

        var generator = new OrderGenerator(shops, areas, random);
        var customers = generator.Customers(options.Orders);
        var (sent, joined, refused, numbers) = (0, 0, new List<string>(), new List<string>());
        for (var i = 0; i < options.Orders; i++)
        {
            var order = generator.Next(customers);
            var command = order.Command;

            // As a shop's checkout does: the quote first, which also says whether the order joins a delivery
            var query = string.Join('&', new Dictionary<string, string?>
            {
                ["phone"] = command.Customer!.Phone,
                ["areaId"] = command.Address!.AreaId?.ToString(CultureInfo.InvariantCulture),
                ["line1"] = command.Address.Line1,
                ["speed"] = command.Speed.ToString().ToLowerInvariant(),
                ["doNotHold"] = command.DoNotHold ? "true" : "false",
                ["weightGrams"] = command.Packages.Sum(p => p.WeightGrams).ToString(CultureInfo.InvariantCulture)
            }.Select(p => $"{p.Key}={Uri.EscapeDataString(p.Value ?? "")}"));
            using var quoteRequest = new HttpRequestMessage(HttpMethod.Get, $"api/v1/quote?{query}");
            quoteRequest.Headers.Add("X-Api-Key", order.Shop.ApiKey);
            using var quoteResponse = await api.SendAsync(quoteRequest, cancellationToken);
            var joins = quoteResponse.IsSuccessStatusCode &&
                (await quoteResponse.Content.ReadFromJsonAsync<JsonElement>(Json, cancellationToken))
                    .GetProperty("joinsDelivery").GetBoolean();

            using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/orders")
            {
                Content = JsonContent.Create(order.Command, options: Json)
            };
            request.Headers.Add("X-Api-Key", order.Shop.ApiKey);
            request.Headers.Add("Idempotency-Key", order.Command.IdempotencyKey);
            using var response = await api.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.Created)
            {
                refused.Add($"{order.Shop.Kind.Name}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}");
                continue;
            }

            var created = await response.Content.ReadFromJsonAsync<JsonElement>(Json, cancellationToken);
            var fee = created.GetProperty("fee").GetDecimal();
            numbers.Add(created.GetProperty("number").GetString()!);
            sent++;
            joined += joins ? 1 : 0;
            log($"{tenant.Slug} {created.GetProperty("number").GetString()} {order.Shop.Kind.Name} " +
                $"for {command.Customer.Name} ({command.Customer.Phone}) {tenant.CurrencyCode} {fee:0}" +
                (joins ? ", joins a delivery" : ""));
            if (options.Pace > TimeSpan.Zero && i < options.Orders - 1)
            {
                await Task.Delay(options.Pace, cancellationToken);
            }
        }

        return new SimulationResult(tenant.Slug, sent, joined, refused, numbers);
    }
}

/// <param name="Shops">How many of the made-up shops each operator gets (at most <see cref="SimulatedShops.Kinds"/>).</param>
/// <param name="Orders">Orders per operator.</param>
/// <param name="Pace">Wait between orders, so a dashboard can be watched filling up.</param>
/// <param name="Seed">Repeats a run's customers and orders; none for a new set each time.</param>
public sealed record SimulationOptions(int Shops, int Orders, TimeSpan Pace, int? Seed);
