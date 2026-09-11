// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.IO;
using System.Linq;

// ReSharper disable StringLiteralTypo
// ReSharper disable MemberCanBePrivate.Global

namespace DeepEquals.Smoke.Tests;

/// <summary>Where the downstream projects live, and what has to exist before they can be built.</summary>
public static class SmokePaths
{
    /// <summary>Long enough for a cold restore of a wasm or self-contained publish on a slow runner.</summary>
    public const int CommandTimeout = 15 * 60 * 1000;

    public const string Configuration = "Release";

    /// <summary>The `downstream` directory, found by walking up from the test binary.</summary>
    public static string DownstreamRoot { get; } = FindDownstreamRoot();

    public static string RepositoryRoot { get; } = Path.GetFullPath(Path.Combine(DownstreamRoot, ".."));

    public static string Consumers { get; } = Path.Combine(DownstreamRoot, "smoke", "consumers");

    public static string PackageFeed { get; } = Path.Combine(RepositoryRoot, "artifacts", "package", Configuration.ToLowerInvariant());

    public static string Consumer(string name) => Path.Combine(Consumers, name);

    /// <summary>The two packages a consumer references, analyzer first.</summary>
    public static readonly string[] PackageIds = ["DeepEquals.SourceGenerator", "DeepEquals.SourceGeneration.Framework"];

    /// <summary>
    /// The version both packages are at in the local feed.
    /// Reading it keeps the failure legible when nothing was packed,
    /// which is the one setup mistake every one of these tests would otherwise fail on obscurely,
    /// and catches a feed holding two versions of a package, where the floating reference would silently pick the newer.
    /// </summary>
    public static string PackageVersion()
    {
        if (!Directory.Exists(PackageFeed))
            throw new InvalidOperationException(
                $"No package feed at {PackageFeed}. Run: dotnet pack DeepEquals.SourceGenerator.slnx -c {Configuration}");

        string? agreed = null;
        foreach (var id in PackageIds)
        {
            // The analyzer id is a prefix of nothing else, but the framework id is not a prefix of the
            // analyzer's, so each pattern is anchored by the version separator that follows the id.
            var packages = Directory.GetFiles(PackageFeed, id + ".*.nupkg")
                .Where(p => char.IsDigit(Path.GetFileNameWithoutExtension(p)[(id.Length + 1)..][0]))
                .ToArray();

            if (packages.Length == 0)
                throw new InvalidOperationException(
                    $"No {id} package in {PackageFeed}. Run: dotnet pack DeepEquals.SourceGenerator.slnx -c {Configuration}");

            if (packages.Length > 1)
                throw new InvalidOperationException(
                    $"{packages.Length} versions of {id} in {PackageFeed}; the floating version cannot pick between them. Delete the stale ones:{Environment.NewLine}" +
                    string.Join(Environment.NewLine, packages.Select(Path.GetFileName)));

            var version = Path.GetFileNameWithoutExtension(packages[0])[(id.Length + 1)..];
            if (agreed is not null && agreed != version)
                throw new InvalidOperationException(
                    $"The feed holds {PackageIds[0]} and {id} at different versions ({agreed} and {version}); pack them together.");

            agreed = version;
        }

        return agreed!;
    }

    private static string FindDownstreamRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (directory.Name == "downstream" &&
                File.Exists(Path.Combine(directory.FullName, "NuGet.config")))
                return directory.FullName;

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"Could not find the downstream directory above {AppContext.BaseDirectory}");
    }
}
