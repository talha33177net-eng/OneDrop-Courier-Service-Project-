using System.Xml.Linq;

namespace Architecture.Tests;

/// <summary>
/// Database.sqlproj lists its .sql files instead of globbing, so Visual Studio shows them. A file on disk
/// that is not listed is silently left out of the dacpac; a listed file that is gone breaks the build for
/// someone else. Both fail here.
/// </summary>
public class DatabaseProjectFileTests
{
    private static readonly string ProjectDirectory = Path.Combine(FindRepositoryRoot(), "src", "Database");

    private static readonly HashSet<string> Listed = XDocument
        .Load(Path.Combine(ProjectDirectory, "Database.sqlproj"))
        .Descendants()
        .Where(element => element.Name.LocalName is "Build" or "PostDeploy" or "PreDeploy")
        .Select(element => Normalise(element.Attribute("Include")!.Value))
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Every_sql_file_on_disk_is_listed()
    {
        var onDisk = Directory
            .EnumerateFiles(ProjectDirectory, "*.sql", SearchOption.AllDirectories)
            .Select(path => Normalise(Path.GetRelativePath(ProjectDirectory, path)))
            .Where(path => !path.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) &&
                           !path.StartsWith("obj/", StringComparison.OrdinalIgnoreCase));

        Assert.Empty(onDisk.Where(path => !Listed.Contains(path)));
    }

    [Fact]
    public void Every_listed_file_exists()
    {
        Assert.Empty(Listed.Where(path => !File.Exists(Path.Combine(ProjectDirectory, path))));
    }

    [Fact]
    public void Every_sql_file_starts_with_the_standard_header()
    {
        var headerless = Listed
            .Where(path => !path.Equals("Script.PostDeployment.sql", StringComparison.OrdinalIgnoreCase))
            .Where(path => !File.ReadLines(Path.Combine(ProjectDirectory, path)).First().TrimStart('﻿').StartsWith("-- =-=-", StringComparison.Ordinal));

        Assert.Empty(headerless);
    }

    private static string Normalise(string path)
    {
        return path.Replace('\\', '/');
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Courier.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Courier.sln not found above the test output.");
    }
}
