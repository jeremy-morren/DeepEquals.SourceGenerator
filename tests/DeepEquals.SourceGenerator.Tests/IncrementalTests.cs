// Copyright 2026 The DeepEquals source generator project contributors. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
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

    private static (GeneratorDriver Driver, CSharpCompilation Compilation) Create(string unrelated, string models = Models, string assemblyName = "Incremental")
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(models, parseOptions, path: "Models.cs"), CSharpSyntaxTree.ParseText(unrelated, parseOptions, path: "Helper.cs")
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

    [Theory]
    [InlineData("CycleHandling = DeepEqualsCycleHandling.Tree")]
    [InlineData("CycleHandling = DeepEqualsCycleHandling.Path")]
    [InlineData("MatchingHashDepth = 2")]
    [InlineData("MaxDepth = 64")]
    public void Changing_an_option_reruns_the_output(string option)
    {
        var (driver, first) = Create(Unrelated);
        driver = driver.RunGenerators(first);

        var second = first.ReplaceSyntaxTree(
            first.SyntaxTrees.Single(t => t.FilePath == "Models.cs"),
            CSharpSyntaxTree.ParseText(Models.Replace("[GenerateDeepEquals(typeof(Person))]", $"[GenerateDeepEquals(typeof(Person))] [DeepEqualsSourceGenerationOptions({option})]"), (CSharpParseOptions)first.SyntaxTrees[0].Options, path: "Models.cs"));
        driver = driver.RunGenerators(second);

        var result = driver.GetRunResult().Results.Single();
        result.TrackedOutputSteps.SelectMany(kv => kv.Value).SelectMany(s => s.Outputs)
            .Should().Contain(o => o.Reason == IncrementalStepRunReason.Modified || o.Reason == IncrementalStepRunReason.New);
        var header = result.GeneratedSources.First().SourceText.ToString();
        header.Should().Contain(option.Split('=')[0].Trim() + " ");
    }

    [Fact]
    public void Cancellation_during_generation_propagates_instead_of_failing_the_context()
    {
        var (driver, compilation) = Create(Unrelated);
        using var source = new System.Threading.CancellationTokenSource();
        source.Cancel();
        var act = () => driver.RunGenerators(compilation, source.Token);
        act.Should().Throw<System.OperationCanceledException>();
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
        result.GeneratedSources.Should().Contain(s => s.SourceText.ToString().Contains("Score"), "the Person file carries the new member");
    }

    /// <summary>
    /// Runs the generator, edits <paramref name="file"/> from <paramref name="before"/> to <paramref name="after"/>, runs it
    /// again, and returns how many analyses the second run added under a unique assembly name.
    /// </summary>
    private static (int Analyses, GeneratorRunResult Result) Edit(string models, string file, string before, string after)
    {
        var assemblyName = "Incremental_" + Guid.NewGuid().ToString("N");
        var (driver, first) = Create(Unrelated, models, assemblyName);
        driver = driver.RunGenerators(first);
        var runs = Analysis.ContextAnalyzer.AnalysisRuns(assemblyName);
        runs.Should().Be(1);

        var tree = first.SyntaxTrees.Single(t => t.FilePath == file);
        var text = tree.ToString();
        text.Should().Contain(before);
        var second = first.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(text.Replace(before, after), (CSharpParseOptions)tree.Options, path: file));
        driver = driver.RunGenerators(second);
        return (Analysis.ContextAnalyzer.AnalysisRuns(assemblyName) - runs, driver.GetRunResult().Results.Single());
    }

    [Fact]
    public void Editing_a_method_body_skips_the_analysis()
    {
        var (analyses, result) = Edit(Models, "Helper.cs", "x * 2", "x + x");
        analyses.Should().Be(0, "a method body declares nothing the closure can see");
        result.GeneratedSources.Should().NotBeEmpty();
    }

    [Fact]
    public void Editing_a_declaration_reruns_the_analysis()
    {
        var (analyses, result) = Edit(Models, "Models.cs", "public int Age;", "public int Age; public double Score;");
        analyses.Should().Be(1);
        result.GeneratedSources.Should().Contain(s => s.SourceText.ToString().Contains("Score"));
    }

    [Fact]
    public void Editing_an_accessor_body_reruns_the_analysis_since_field_declares_storage()
    {
        const string models = """
            using DeepEquals.SourceGeneration;
            namespace Tests;
            public sealed class Person { private int _age; public int Age { get { return _age; } } }
            [GenerateDeepEquals(typeof(Person))]
            public partial class Ctx : DeepEqualsContextBase { }
            """;
        var (analyses, result) = Edit(models, "Models.cs", "get { return _age; }", "get { return field; }");
        analyses.Should().Be(1);
        result.GeneratedSources.Should().Contain(s => s.SourceText.ToString().Contains("<Age>k__BackingField"), "the accessor's field keyword created a backing field");
    }

    [Fact]
    public void Editing_a_body_that_captures_a_primary_constructor_parameter_reruns_the_analysis()
    {
        const string models = """
            using DeepEquals.SourceGeneration;
            namespace Tests;
            public sealed class Box(int seed) { public int Value; public int Get() => Value; }
            [GenerateDeepEquals(typeof(Box))]
            public partial class Ctx : DeepEqualsContextBase { }
            """;
        var (analyses, result) = Edit(models, "Models.cs", "public int Get() => Value;", "public int Get() => seed;");
        analyses.Should().Be(1);
        result.GeneratedSources.Should().Contain(s => s.SourceText.ToString().Contains("<seed>P"), "the body captured the parameter into a field");
    }

    [Fact]
    public void Changing_a_directive_reruns_the_analysis()
    {
        var (analyses, _) = Edit(Models, "Models.cs", "namespace Tests;", "#nullable disable\nnamespace Tests;");
        analyses.Should().Be(1, "a directive changes what a declaration means");
    }
}
