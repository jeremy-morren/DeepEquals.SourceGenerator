using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Xunit.Abstractions;

namespace DeepEquals.Smoke.Tests;

/// <summary>The result of one SDK invocation: everything the process wrote, plus its exit code.</summary>
public sealed record CommandResult(int ExitCode, string Output)
{
    public IEnumerable<string> Lines => Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

    /// <summary>The lines carrying a trimming or AOT diagnostic, in the order the SDK reported them.</summary>
    public IEnumerable<string> TrimmingWarnings
    {
        get
        {
            foreach (string line in Lines)
            {
                if (System.Text.RegularExpressions.Regex.IsMatch(line, @"\b(IL2\d{3}|IL3\d{3})\b"))
                {
                    yield return line.Trim();
                }
            }
        }
    }
}

/// <summary>Runs the SDK against the smoke consumers and captures what it said.</summary>
public static class Dotnet
{
    /// <summary>
    /// Runs `dotnet` with the given arguments and returns its combined output. Never throws on a
    /// non-zero exit: several tests are about what a failing or warning build reported.
    /// </summary>
    public static CommandResult Run(ITestOutputHelper output, string arguments, string? workingDirectory = null, IDictionary<string, string>? environment = null)
    {
        ProcessStartInfo start = new()
        {
            FileName = "dotnet",
            Arguments = arguments,
            WorkingDirectory = workingDirectory ?? SmokePaths.SmokeRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        // The SDK picks up the ambient build's variables otherwise, and a smoke consumer must not
        // inherit anything from the build that produced the package.
        start.Environment.Remove("MSBuildSDKsPath");
        start.Environment.Remove("MSBuildExtensionsPath");
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";

        if (environment is not null)
        {
            foreach (KeyValuePair<string, string> pair in environment)
            {
                start.Environment[pair.Key] = pair.Value;
            }
        }

        output.WriteLine($"$ dotnet {arguments}");

        StringBuilder captured = new();
        using Process process = new() { StartInfo = start };
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) { lock (captured) { captured.Append(e.Data).Append('\n'); } } };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) { lock (captured) { captured.Append(e.Data).Append('\n'); } } };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        if (!process.WaitForExit(SmokePaths.CommandTimeout))
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw new TimeoutException($"dotnet {arguments} did not finish within {SmokePaths.CommandTimeout / 1000}s");
        }

        // WaitForExit(int) can return before the redirected streams are drained.
        process.WaitForExit();

        string text;
        lock (captured) { text = captured.ToString(); }
        output.WriteLine(text);

        return new CommandResult(process.ExitCode, text);
    }

    /// <summary>
    /// Publishes a consumer from a clean slate. Every caller asserts on which diagnostics the build
    /// reported, and an incremental publish skips the compiler, so a second run of the same test would
    /// see no warnings at all and quietly pass. Clearing obj also forces the freshly packed package to
    /// be restored rather than resolved from the last run's assets file.
    /// </summary>
    public static CommandResult Publish(ITestOutputHelper output, string project)
    {
        foreach (string generated in new[] { "obj", "bin" })
        {
            string path = Path.Combine(project, generated);
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }

        return Run(output, $"publish \"{project}\" -c {SmokePaths.Configuration}");
    }

    /// <summary>Runs an executable the SDK produced, and returns what it printed.</summary>
    public static CommandResult RunExecutable(ITestOutputHelper output, string path, IDictionary<string, string>? environment = null)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"the consumer did not produce {path}", path);
        }

        ProcessStartInfo start = new()
        {
            FileName = path,
            WorkingDirectory = Path.GetDirectoryName(path),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        if (environment is not null)
        {
            foreach (KeyValuePair<string, string> pair in environment)
            {
                start.Environment[pair.Key] = pair.Value;
            }
        }

        output.WriteLine($"$ {path}");

        using Process process = Process.Start(start)!;
        string text = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        output.WriteLine(text);

        return new CommandResult(process.ExitCode, text);
    }
}
