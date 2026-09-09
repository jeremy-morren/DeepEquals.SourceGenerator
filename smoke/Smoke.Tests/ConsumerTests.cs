// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace DeepEquals.Smoke.Tests;

/// <summary>
/// A clean consumer of the packed package on every target the package claims. This is the check that
/// analyzer placement, runtime asset selection and transitive dependencies are right, none of which
/// the repository's own build exercises: it references the projects, not the package.
/// </summary>
[Trait("Category", "Consumer")]
public sealed class ConsumerTests
{
    private readonly ITestOutputHelper _output;

    public ConsumerTests(ITestOutputHelper output) => _output = output;

    /// <summary>net6.0 and net7.0 are built here but have no installed runtime of their own.</summary>
    private static readonly Dictionary<string, string> RollForward = new() { ["DOTNET_ROLL_FORWARD"] = "Major" };

    public static TheoryData<string> Runnable => new() { "net6.0", "net7.0", "net8.0", "net10.0" };

    public static TheoryData<string> Libraries => new() { "netstandard2.0", "netstandard2.1" };

    [Fact]
    public void The_feed_holds_exactly_one_package()
    {
        string version = SmokePaths.PackageVersion();
        _output.WriteLine($"package version {version}");
        version.Should().NotBeNullOrWhiteSpace();
    }

    [Theory]
    [MemberData(nameof(Libraries))]
    public void A_netstandard_library_compiles_against_the_package(string framework)
    {
        // A netstandard assembly cannot run; building proves the emitted code compiles against that
        // reference surface, which is the whole claim for these two tiers.
        CommandResult build = ConsumerProject.Build(_output, framework);

        build.ExitCode.Should().Be(0, "the {0} library consumer must build against the package", framework);
    }

    [Theory]
    [MemberData(nameof(Runnable))]
    public void A_consumer_application_runs_the_generated_comparers(string framework)
    {
        CommandResult build = ConsumerProject.Build(_output, framework);
        build.ExitCode.Should().Be(0, "the {0} consumer must build against the package", framework);

        CommandResult run = Dotnet.RunExecutable(_output, ConsumerProject.Executable(framework), RollForward);

        run.Output.Should().Contain("OK ", "the assertions must pass on {0}", framework);
        run.ExitCode.Should().Be(0, "the {0} consumer must exit cleanly", framework);
    }

    [Fact]
    public void A_net472_consumer_runs_on_dotnet_framework()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            // Elsewhere the same executable is covered by MonoTests, on the runtime that runs it there.
            _output.WriteLine("not Windows; the net472 consumer is covered by MonoTests here");
            return;
        }

        CommandResult build = ConsumerProject.Build(_output, "net472");
        build.ExitCode.Should().Be(0, "the net472 consumer must build against the package");

        CommandResult run = Dotnet.RunExecutable(_output, ConsumerProject.Executable("net472"));

        run.Output.Should().Contain("OK ");
        run.ExitCode.Should().Be(0);
    }
}

/// <summary>Building and locating the one consumer project that covers every claimed target.</summary>
internal static class ConsumerProject
{
    public static string Directory => SmokePaths.Consumer("Consumer");

    public static CommandResult Build(ITestOutputHelper output, string framework) =>
        Dotnet.Run(output, $"build \"{Directory}\" -c {SmokePaths.Configuration} -f {framework}");

    public static string Executable(string framework)
    {
        // net472 always produces an .exe; a .NET application gets an apphost named for the platform.
        bool exe = framework == "net472" || RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        return Path.Combine(Directory, "bin", SmokePaths.Configuration, framework, "Consumer" + (exe ? ".exe" : string.Empty));
    }
}
