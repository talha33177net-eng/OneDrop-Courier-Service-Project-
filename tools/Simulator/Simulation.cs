using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Application.Abstractions;
using Application.Network.ListAreas;

namespace Simulator;

/// <param name="Sent">Parcels the API booked.</param>
/// <param name="Charges">What the booked parcels would cost their shops if all were delivered.</param>
/// <param name="Refused">Parcels the API refused, with its answer.</param>
/// <param name="TrackingCodes">The booked parcels' tracking codes.</param>
public sealed record SimulationResult(
    string Tenant, int Sent, decimal Charges, IReadOnlyList<string> Refused, IReadOnlyList<string> TrackingCodes);

/// <summary>One courier's run: make sure its shops exist, then book the parcels through the API, one by one.</summary>
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

        var generator = new ParcelGenerator(shops, areas, random);
        var (sent, charges, refused, codes) = (0, 0m, new List<string>(), new List<string>());
        for (var i = 0; i < options.Parcels; i++)
        {
            var parcel = generator.Next();
            var command = parcel.Command;

            // As a shop's system does: the charge first, then the booking
            var query = $"areaId={command.AreaId}&weightKg={command.WeightKg.ToString(CultureInfo.InvariantCulture)}" +
                $"&codAmount={command.CodAmount.ToString(CultureInfo.InvariantCulture)}";
            using var chargeRequest = new HttpRequestMessage(HttpMethod.Get, $"api/v1/charge?{query}");
            chargeRequest.Headers.Add("X-Api-Key", parcel.Shop.ApiKey);
            using var chargeResponse = await api.SendAsync(chargeRequest, cancellationToken);
            chargeResponse.EnsureSuccessStatusCode();

            using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/parcels")
            {
                Content = JsonContent.Create(command, options: Json)
            };
            request.Headers.Add("X-Api-Key", parcel.Shop.ApiKey);
            request.Headers.Add("Idempotency-Key", command.IdempotencyKey);
            using var response = await api.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.Created)
            {
                refused.Add($"{parcel.Shop.Kind.Name}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(cancellationToken)}");
                continue;
            }

            var created = await response.Content.ReadFromJsonAsync<JsonElement>(Json, cancellationToken);
            var code = created.GetProperty("trackingCode").GetString()!;
            var charge = created.GetProperty("totalCharge").GetDecimal();
            codes.Add(code);
            sent++;
            charges += charge;
            log($"{tenant.Slug} {code} {parcel.Shop.Kind.Name} to {created.GetProperty("area").GetString()} " +
                $"COD {command.CodAmount:0}, charge {tenant.CurrencyCode} {charge:0}");
            if (options.Pace > TimeSpan.Zero && i < options.Parcels - 1)
            {
                await Task.Delay(options.Pace, cancellationToken);
            }
        }

        return new SimulationResult(tenant.Slug, sent, charges, refused, codes);
    }
}

/// <param name="Shops">How many of the made-up shops each courier gets (at most <see cref="SimulatedShops.Kinds"/>).</param>
/// <param name="Parcels">Parcels per courier.</param>
/// <param name="Pace">Wait between parcels, so a dashboard can be watched filling up.</param>
/// <param name="Seed">Repeats a run's parcels; none for a new set each time.</param>
public sealed record SimulationOptions(int Shops, int Parcels, TimeSpan Pace, int? Seed);
