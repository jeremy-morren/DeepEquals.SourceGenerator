using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Playwright;
using Xunit;
using Xunit.Abstractions;

namespace DeepEquals.Smoke.Tests;

/// <summary>
/// Blazor WebAssembly, which runs Mono compiled to wasm and is the only 32-bit target this package
/// claims. Publishing is where a generated context would be reported as trim-unsafe for a real Blazor
/// consumer, and loading the published application in a browser is what proves the comparers run
/// there rather than merely compile for it.
/// </summary>
[Trait("Category", "Wasm")]
public sealed class BlazorWasmTests
{
    private readonly ITestOutputHelper _output;

    public BlazorWasmTests(ITestOutputHelper output) => _output = output;

    private static string Project => SmokePaths.Consumer("BlazorWasm");

    private static string PublishedWebRoot =>
        Path.Combine(Project, "bin", SmokePaths.Configuration, "net10.0", "publish", "wwwroot");

    [Fact]
    public async Task The_published_application_runs_the_comparers_in_a_browser()
    {
        CommandResult publish = Dotnet.Publish(_output, Project);
        publish.ExitCode.Should().Be(0, "the Blazor consumer must publish");

        // Blazor's own scaffolding warns about its router whatever the application references. Only a
        // warning whose message names this package, or that points inside a file this generator
        // emitted, belongs to this repository. Neither the leading path nor the trailing project name
        // is matched on its own, because the checkout directory can be called anything.
        List<string> ours = publish.TrimmingWarnings.Where(IsOurs).ToList();
        ours.Should().BeEmpty("a Blazor consumer must not be warned about a generated context");

        Directory.Exists(PublishedWebRoot).Should().BeTrue("the publish must produce {0}", PublishedWebRoot);

        await using StaticSite site = await StaticSite.StartAsync(PublishedWebRoot);
        _output.WriteLine($"serving {PublishedWebRoot} at {site.Url}");

        using IPlaywright playwright = await Playwright.CreateAsync();
        await using IBrowser browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
        IPage page = await browser.NewPageAsync();

        page.Console += (_, message) => _output.WriteLine($"[console] {message.Text}");
        page.PageError += (_, error) => _output.WriteLine($"[page error] {error}");

        IResponse? response = await page.GotoAsync(site.Url, new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        response.Should().NotBeNull();
        response!.Status.Should().Be(200);

        // The element appears only once the assertions have run on the WebAssembly runtime.
        await page.WaitForSelectorAsync("#result", new PageWaitForSelectorOptions { Timeout = 120_000 });
        string result = (await page.InnerTextAsync("#result")).Trim();

        _output.WriteLine($"page reported: {result}");

        result.Should().StartWith("OK ", "the assertions must pass in the browser");
        result.Should().Contain("pointer=32-bit", "Blazor WebAssembly is the 32-bit target");
    }

    private static bool IsOurs(string warning)
    {
        string message = System.Text.RegularExpressions.Regex.Match(warning, @": warning IL\d+: (?<text>.*?)(?: \[[^\[\]]*\])?$").Groups["text"].Value;
        bool emitted = warning.Contains(Path.Combine("DeepEquals.SourceGenerator", "DeepEquals.SourceGenerator.DeepEqualsGenerator"), StringComparison.Ordinal)
            || warning.Contains("DeepEquals.SourceGenerator/DeepEquals.SourceGenerator.DeepEqualsGenerator", StringComparison.Ordinal);

        return message.Contains("DeepEquals", StringComparison.Ordinal) || emitted;
    }
}
