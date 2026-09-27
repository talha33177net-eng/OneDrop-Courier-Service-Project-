using System.Reflection;
using DbUp;
using Microsoft.Extensions.Configuration;

namespace DatabaseUpdate;

/// <summary>
/// Runs the embedded data migrations, each exactly once per database (journal: dbo.SchemaVersions).
///
///   dbup pre   Scripts/Pre only, BEFORE the dacpac publish, against the old schema. For intentional data-loss
///              changes (dropping or renaming a column) that the publish's BlockOnPossibleDataLoss would refuse.
///              Creates an empty database first if there is none, so a first deploy works from nothing.
///   dbup       Everything except Scripts/Pre, AFTER the publish. Seed and data migrations.
///
/// Exit code 0 on success, 1 on failure, so a pipeline stops on a failed migration.
/// </summary>
public static class Program
{
    private const string ScriptPrefix = "DatabaseUpdate.Scripts.";
    private const string PreScriptPrefix = "DatabaseUpdate.Scripts.Pre.";

    public static int Main(string[] args)
    {
        try
        {
            var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
                ?? Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
                ?? "Development";

            // Beside the executable, not the working directory: a pipeline runs this from its own workspace
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile($"appsettings.{environment}.json", optional: true)
                .AddJsonFile("appsettings.Local.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            var connectionString = configuration.GetConnectionString("Database");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                Write(ConsoleColor.Red, "No ConnectionStrings:Database configured.");
                return 1;
            }

            var runPre = args.Any(a => a.Equals("pre", StringComparison.OrdinalIgnoreCase));
            Write(ConsoleColor.Cyan, $"Environment {environment}: running the {(runPre ? "PRE-SCHEMA" : "post-schema")} phase");

            if (runPre)
            {
                EnsureDatabase.For.SqlDatabase(connectionString);
            }

            var upgrader = DeployChanges.To
                .SqlDatabase(connectionString)
                .WithScriptsEmbeddedInAssembly(Assembly.GetExecutingAssembly(), script => runPre
                    ? script.StartsWith(PreScriptPrefix, StringComparison.Ordinal)
                    : script.StartsWith(ScriptPrefix, StringComparison.Ordinal) &&
                      !script.StartsWith(PreScriptPrefix, StringComparison.Ordinal))
                .WithExecutionTimeout(TimeSpan.FromMinutes(30))
                .WithTransaction()
                .LogToConsole()
                .Build();

            if (!upgrader.IsUpgradeRequired())
            {
                Write(ConsoleColor.Green, "Database is up to date.");
                return 0;
            }

            var result = upgrader.PerformUpgrade();
            if (!result.Successful)
            {
                Write(ConsoleColor.Red, $"Migration failed: {result.Error}");
                return 1;
            }

            Write(ConsoleColor.Green, $"Ran {result.Scripts.Count()} script(s).");

            return 0;
        }
        catch (Exception exception)
        {
            Write(ConsoleColor.Red, $"Fatal error: {exception}");
            return 1;
        }
    }

    private static void Write(ConsoleColor color, string message)
    {
        Console.ForegroundColor = color;
        Console.WriteLine(message);
        Console.ResetColor();
    }
}
