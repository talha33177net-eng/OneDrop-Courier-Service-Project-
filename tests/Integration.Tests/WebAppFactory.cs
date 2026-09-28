using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

[assembly: AssemblyFixture(typeof(Integration.Tests.WebAppFactory))]

namespace Integration.Tests;

/// <summary>
/// The real web app against the test database: INTEGRATION_TEST_DB when set (CI, another developer's database),
/// otherwise ConnectionStrings:Database in testsettings.Local.json (git-ignored; OneDrop-Test on ras-x2).
/// Development settings are used, so the demo seeder creates the two tenants' merchants and API keys on first
/// start (the launch tenants come from dbup).
/// </summary>
public sealed class WebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string DhakaFashion = "od_dhkfashion01_DevOnlyKeyDoNotUseInProduction01";
    public const string DhakaGadget = "od_dhkgadget001_DevOnlyKeyDoNotUseInProduction02";
    public const string DhakaBeauty = "od_dhkbeauty001_DevOnlyKeyDoNotUseInProduction03";
    public const string ChattogramFashion = "od_ctgfashion01_DevOnlyKeyDoNotUseInProduction04";

    public static string? ConnectionString { get; } = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("testsettings.json", optional: true)
        .AddJsonFile("testsettings.Local.json", optional: true)
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = Environment.GetEnvironmentVariable("INTEGRATION_TEST_DB")
        }.Where(setting => !string.IsNullOrWhiteSpace(setting.Value)))
        .Build()
        .GetConnectionString("Database");

    /// <summary>First line of every test: without a test database the test is reported as skipped.</summary>
    public static void RequireDatabase()
    {
        Assert.SkipWhen(
            string.IsNullOrWhiteSpace(ConnectionString),
            "No test database configured. Create testsettings.Local.json or set INTEGRATION_TEST_DB, and publish it with " +
            "./tools/db/publish.ps1 -Database OneDrop-Test.");
    }

    /// <summary>
    /// Starts the host once, before tests run in parallel. WebApplicationFactory starts it lazily without a lock,
    /// so parallel first calls each started a host and their demo seeders raced on an empty database.
    /// </summary>
    public ValueTask InitializeAsync()
    {
        if (!string.IsNullOrWhiteSpace(ConnectionString))
        {
            _ = Services;
        }

        return ValueTask.CompletedTask;
    }

    public HttpClient ClientFor(string apiKey)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", apiKey);

        return client;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Database"] = ConnectionString,
            ["Seed:DemoData"] = "true",
            // No job server: jobs are run by the tests themselves, with a fake clock
            ["Jobs:Server"] = "false",
            ["Seed:Password"] = "OneDrop#2026"
        };

        builder.UseEnvironment("Development");

        // Added after the web app's own sources (including its appsettings.Local.json, which points at the dev
        // database), so the test database always wins
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(settings));
        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }
    }
}
