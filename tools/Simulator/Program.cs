using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Application;
using Application.Abstractions;
using Infrastructure;

namespace Simulator;

/// <summary>
/// dotnet run --project tools/Simulator -- [--tenant onedrop] [--shops 6] [--parcels 40] [--pace 2] [--seed 7]
///                                         [--url http://localhost:5080]
/// The app must be running at --url. The database is the app's: ConnectionStrings:Database from
/// src/Web/appsettings.Local.json, or the ConnectionStrings__Database environment variable.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var arguments = new ConfigurationBuilder().AddCommandLine(args).Build();
        var root = FindRepositoryRoot();
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(root, "src", "Web", "appsettings.json"), optional: false)
            .AddJsonFile(Path.Combine(root, "src", "Web", "appsettings.Local.json"), optional: true)
            .AddEnvironmentVariables()
            .Build();
        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("Database")))
        {
            Console.Error.WriteLine("No ConnectionStrings:Database: create src/Web/appsettings.Local.json (see README).");
            return 1;
        }

        var options = new SimulationOptions(
            Math.Clamp(int.Parse(arguments["shops"] ?? "6", CultureInfo.InvariantCulture), 1, SimulatedShops.Kinds.Length),
            Math.Max(1, int.Parse(arguments["parcels"] ?? "40", CultureInfo.InvariantCulture)),
            TimeSpan.FromSeconds(double.Parse(arguments["pace"] ?? "0", CultureInfo.InvariantCulture)),
            arguments["seed"] is { } seed ? int.Parse(seed, CultureInfo.InvariantCulture) : null);

        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddLogging()
            .AddApplication()
            .AddInfrastructure()
            .BuildServiceProvider();
        var catalog = services.GetRequiredService<ITenantCatalog>();
        IReadOnlyList<TenantInfo> tenants = arguments["tenant"] is { } slug
            ? [await catalog.FindBySlugAsync(slug) ?? throw new InvalidOperationException($"There is no courier '{slug}'.")]
            : await catalog.ListAsync();

        using var api = new HttpClient { BaseAddress = new Uri((arguments["url"] ?? "http://localhost:5080").TrimEnd('/') + "/") };
        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, press) =>
        {
            press.Cancel = true;
            stop.Cancel();
        };

        Console.WriteLine($"Simulating {options.Parcels} parcels from {options.Shops} shops for " +
            $"{string.Join(", ", tenants.Select(t => t.Name))} through {api.BaseAddress}");
        var failed = false;
        foreach (var tenant in tenants)
        {
            var result = await Simulation.RunAsync(services, api, tenant, options, Console.WriteLine, stop.Token);
            Console.WriteLine($"{tenant.Name}: {result.Sent} parcels booked, {tenant.CurrencyCode} {result.Charges:0} in charges, " +
                $"{result.Refused.Count} refused");
            result.Refused.ToList().ForEach(Console.Error.WriteLine);
            failed |= result.Refused.Count > 0;
        }

        return failed ? 1 : 0;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Courier.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Run the simulator from inside the repository.");
    }
}
