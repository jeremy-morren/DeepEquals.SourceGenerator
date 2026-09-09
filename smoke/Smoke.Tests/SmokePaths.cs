using System;
using System.IO;
using System.Linq;

namespace DeepEquals.Smoke.Tests;

/// <summary>Where the smoke consumers live, and what has to exist before they can be built.</summary>
public static class SmokePaths
{
    /// <summary>Long enough for a cold restore of a wasm or self-contained publish on a slow runner.</summary>
    public const int CommandTimeout = 15 * 60 * 1000;

    public const string Configuration = "Release";

    /// <summary>The `smoke` directory, found by walking up from the test binary.</summary>
    public static string SmokeRoot { get; } = FindSmokeRoot();

    public static string RepositoryRoot { get; } = Path.GetFullPath(Path.Combine(SmokeRoot, ".."));

    public static string Consumers { get; } = Path.Combine(SmokeRoot, "consumers");

    public static string PackageFeed { get; } = Path.Combine(RepositoryRoot, "artifacts", "package", Configuration.ToLowerInvariant());

    public static string Consumer(string name) => Path.Combine(Consumers, name);

    /// <summary>
    /// The version in the local feed. Reading it keeps the failure legible when nothing was packed,
    /// which is the one setup mistake every one of these tests would otherwise fail on obscurely.
    /// </summary>
    public static string PackageVersion()
    {
        if (!Directory.Exists(PackageFeed))
        {
            throw new InvalidOperationException(
                $"No package feed at {PackageFeed}. Run: dotnet pack src/DeepEquals.SourceGeneration.Framework -c {Configuration}");
        }

        string[] packages = Directory.GetFiles(PackageFeed, "DeepEquals.SourceGenerator.*.nupkg");
        if (packages.Length == 0)
        {
            throw new InvalidOperationException(
                $"No DeepEquals.SourceGenerator package in {PackageFeed}. Run: dotnet pack src/DeepEquals.SourceGeneration.Framework -c {Configuration}");
        }

        if (packages.Length > 1)
        {
            throw new InvalidOperationException(
                $"{packages.Length} packages in {PackageFeed}; the floating version cannot pick between them. Delete the stale ones:{Environment.NewLine}" +
                string.Join(Environment.NewLine, packages.Select(Path.GetFileName)));
        }

        return Path.GetFileNameWithoutExtension(packages[0])["DeepEquals.SourceGenerator.".Length..];
    }

    private static string FindSmokeRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (directory.Name == "smoke" && File.Exists(Path.Combine(directory.FullName, "NuGet.config")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find the smoke directory above {AppContext.BaseDirectory}");
    }
}
