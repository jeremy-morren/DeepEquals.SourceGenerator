// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System.Linq;
using System.Reflection;

namespace DeepEquals.SourceGenerator;

/// <summary>The generator's own package version, which the referenced framework package must match.</summary>
internal static class GeneratorInfo
{
    /// <summary>The package version, read once from the assembly's informational version without its build metadata.</summary>
    public static readonly string Version = ReadVersion();

    /// <summary>
    /// Set by tests only: stands in for <see cref="Version"/> so a mismatch can be provoked without a second framework
    /// build. Async-local, so a test that sets it cannot leak the value into tests running beside it.
    /// </summary>
    private static readonly System.Threading.AsyncLocal<string?> s_versionOverride = new();

    internal static string? VersionOverride
    {
        get => s_versionOverride.Value;
        set => s_versionOverride.Value = value;
    }

    public static string EffectiveVersion => VersionOverride ?? Version;

    private static string ReadVersion()
    {
        var assembly = typeof(GeneratorInfo).Assembly;
        var informational = assembly
            .GetCustomAttributes<AssemblyInformationalVersionAttribute>()
            .Select(x => x.InformationalVersion)
            .FirstOrDefault();
        return WithoutBuildMetadata(informational) ?? assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    /// <summary>Drops the <c>+sha</c> build metadata a package version carries, or returns null for an empty version.</summary>
    public static string? WithoutBuildMetadata(string? version)
    {
        if (string.IsNullOrEmpty(version))
            return null;

        var plus = version!.IndexOf('+');
        return plus < 0 ? version : version.Substring(0, plus);
    }
}
