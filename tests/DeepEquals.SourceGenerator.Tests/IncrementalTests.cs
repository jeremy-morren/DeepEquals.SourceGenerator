// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DeepEquals.SourceGenerator.Tests;

/// <summary>The output step must be cached when nothing the model depends on changed, and rerun when a reachable member changes.</summary>
public sealed class IncrementalTests
{
    private const string Models = """
        using System.Collections.Generic;
        using DeepEquals.SourceGeneration;
        namespace Tests;
        public sealed class Person { public string? Name; public int Age; }
        [GenerateDeepEquals(typeof(Person))]
        public partial class Ctx : DeepEqualsContextBase { }
        """;

    private const string Unrelated = """
        namespace Tests;
        public static class Helper { public static int Twice(int x) => x * 2; }
        """;

    private static (GeneratorDriver Driver, CSharpCompilation Compilation) Create(string unrelated)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create(
            "Incremental",
            [CSharpSyntaxTree.ParseText(Models, parseOptions, path: "Models.cs"), CSharpSyntaxTree.ParseText(unrelated, parseOptions, path: "Helper.cs")
            ],
            GeneratorHost.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            [new DeepEqualsGenerator().AsSourceGenerator()],
            parseOptions: parseOptions,
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
        return (driver, compilation);
    }

    [Fact]
    public void Editing_an_unrelated_method_body_leaves_the_output_cached()
    {
        var (driver, first) = Create(Unrelated);
        driver = driver.RunGenerators(first);
        driver.GetRunResult().Diagnostics.Should().BeEmpty();

        var second = first.ReplaceSyntaxTree(
            first.SyntaxTrees.Single(t => t.FilePath == "Helper.cs"),
            CSharpSyntaxTree.ParseText(Unrelated.Replace("x * 2", "x + x"), (CSharpParseOptions)first.SyntaxTrees[0].Options, path: "Helper.cs"));
        driver = driver.RunGenerators(second);

        var result = driver.GetRunResult().Results.Single();
        var outputSteps = result.TrackedOutputSteps.SelectMany(kv => kv.Value).ToImmutableArray();
        outputSteps.Should().NotBeEmpty();
        outputSteps.SelectMany(s => s.Outputs).Should().OnlyContain(o => o.Reason == IncrementalStepRunReason.Cached || o.Reason == IncrementalStepRunReason.Unchanged,
            "an unchanged model must not re-emit");
    }

    [Fact]
    public void Editing_a_reachable_member_reruns_the_output()
    {
        var (driver, first) = Create(Unrelated);
        driver = driver.RunGenerators(first);

        var second = first.ReplaceSyntaxTree(
            first.SyntaxTrees.Single(t => t.FilePath == "Models.cs"),
            CSharpSyntaxTree.ParseText(Models.Replace("public int Age;", "public int Age; public double Score;"), (CSharpParseOptions)first.SyntaxTrees[0].Options, path: "Models.cs"));
        driver = driver.RunGenerators(second);

        var result = driver.GetRunResult().Results.Single();
        result.TrackedOutputSteps.SelectMany(kv => kv.Value).SelectMany(s => s.Outputs)
            .Should().Contain(o => o.Reason == IncrementalStepRunReason.Modified || o.Reason == IncrementalStepRunReason.New);
        result.GeneratedSources.Single().SourceText.ToString().Should().Contain("Score");
    }
}
