// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Diagnostics;
using System.IO;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace DeepEquals.Smoke.Tests;

/// <summary>
/// The netstandard2.0 asset under the other runtime that executes it. Everything that exists because
/// .NET Framework lacks the modern helpers — Marvin32 string hashing, the decimal bit arrays, the
/// pooled buffers, the span work with nothing beneath it — runs on Mono here, where struct layout,
/// string hashing and <c>Unsafe</c> are a separate implementation from CoreCLR's.
/// </summary>
[Trait("Category", "Mono")]
public sealed class MonoTests
{
    private readonly ITestOutputHelper _output;

    public MonoTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void A_net472_consumer_runs_under_mono()
    {
        string? mono = FindMono();
        if (mono is null)
        {
            // Skipping keeps a Windows developer from needing Mono to see the rest of the suite pass.
            // CI sets SMOKE_REQUIRE_MONO, because a runner image that quietly stopped shipping Mono
            // would otherwise turn this into a test that passes by doing nothing.
            Environment.GetEnvironmentVariable("SMOKE_REQUIRE_MONO").Should().BeNullOrEmpty(
                "SMOKE_REQUIRE_MONO is set, so mono was expected on PATH");

            _output.WriteLine("mono is not on PATH; skipping");
            return;
        }

        _output.WriteLine($"using {mono}");

        CommandResult build = ConsumerProject.Build(_output, "net472");
        build.ExitCode.Should().Be(0, "the net472 consumer must build against the package");

        string executable = ConsumerProject.Executable("net472");
        File.Exists(executable).Should().BeTrue("the net472 consumer must produce {0}", executable);

        CommandResult run = Run(mono, executable);

        run.Output.Should().Contain("OK ", "the assertions must pass under mono");
        run.Output.Should().Contain("runtime=mono", "the executable must actually be running on Mono");
        run.ExitCode.Should().Be(0);
    }

    private CommandResult Run(string mono, string executable)
    {
        ProcessStartInfo start = new()
        {
            FileName = mono,
            WorkingDirectory = Path.GetDirectoryName(executable),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(executable);

        _output.WriteLine($"$ {mono} {executable}");

        using Process process = Process.Start(start)!;
        string text = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        _output.WriteLine(text);

        return new CommandResult(process.ExitCode, text);
    }

    private static string? FindMono()
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path is null)
        {
            return null;
        }

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (string name in new[] { "mono", "mono.exe" })
            {
                string candidate = Path.Combine(directory, name);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        return null;
    }
}
