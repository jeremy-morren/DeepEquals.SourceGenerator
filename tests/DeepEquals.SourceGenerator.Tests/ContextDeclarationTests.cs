// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DeepEquals.SourceGenerator.Tests;

/// <summary>
/// The shapes of a context declaration the audit found broken: keyword names, containers that are not classes,
/// contexts differing only by case, a null option array, and the reserved-identifier table. Each once produced an
/// invalid hint name, invalid C#, or an unreported collision.
/// </summary>
public sealed class ContextDeclarationTests
{
    private const string Using = "using DeepEquals.SourceGeneration; using System.Collections.Generic; ";

    [Theory]
    [InlineData("keyword context", "[GenerateDeepEquals(typeof(int))] public partial class @class : DeepEqualsContextBase { }")]
    [InlineData("keyword container", "public partial class @namespace { [GenerateDeepEquals(typeof(int))] public partial class C : DeepEqualsContextBase { } }")]
    [InlineData("keyword namespace", "namespace @event { [GenerateDeepEquals(typeof(int))] public partial class C : DeepEqualsContextBase { } }")]
    [InlineData("struct container", "public partial struct Outer { [GenerateDeepEquals(typeof(int))] public partial class C : DeepEqualsContextBase { } }")]
    [InlineData("record container", "public partial record Outer { [GenerateDeepEquals(typeof(int))] public partial class C : DeepEqualsContextBase { } }")]
    [InlineData("record struct container", "public partial record struct Outer { [GenerateDeepEquals(typeof(int))] public partial class C : DeepEqualsContextBase { } }")]
    [InlineData("interface container", "public partial interface IOuter { [GenerateDeepEquals(typeof(int))] public partial class C : DeepEqualsContextBase { } }")]
    [InlineData("nested twice", "public partial class A { public partial struct B { [GenerateDeepEquals(typeof(int))] public partial class C : DeepEqualsContextBase { } } }")]
    public void Keyword_named_context_namespace_and_container_generate_and_compile(string scenario, string body)
    {
        var run = GeneratorHost.Run(Using + body, load: false);
        run.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty(scenario);
        run.CompileErrors.Should().BeEmpty(scenario);
        run.Result.GeneratedTrees.Should().NotBeEmpty(scenario);
        run.Result.Results.Single().GeneratedSources.Should().OnlyContain(s => !s.HintName.Contains('@'), "a hint name never carries the escape");
    }

    [Fact]
    public void Context_nested_in_struct_and_record_emits_the_right_container()
    {
        var run = GeneratorHost.Run(Using + "public partial struct Outer { public partial record Mid { [GenerateDeepEquals(typeof(int))] public partial class C : DeepEqualsContextBase { } } }", load: false);
        run.CompileErrors.Should().BeEmpty();
        run.GeneratedSource.Should().Contain("partial struct Outer").And.Contain("partial record Mid").And.NotContain("partial class Outer");
    }

    [Fact]
    public void Non_partial_container_reports_its_own_error()
    {
        var run = GeneratorHost.Run(Using + "public class Outer { [GenerateDeepEquals(typeof(int))] public partial class C : DeepEqualsContextBase { } }", load: false);
        var error = run.GeneratorDiagnostics.Single(d => d.Id == "DEQ002");
        error.GetMessage().Should().Contain("Outer").And.Contain("must be partial");
        run.Result.GeneratedTrees.Should().BeEmpty();
    }

    [Fact]
    public void Contexts_differing_only_by_case_get_distinct_hint_names()
    {
        var run = GeneratorHost.Run(Using + "[GenerateDeepEquals(typeof(int))] public partial class C : DeepEqualsContextBase { } [GenerateDeepEquals(typeof(int))] public partial class c : DeepEqualsContextBase { }", load: false);
        run.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        run.CompileErrors.Should().BeEmpty();

        var names = run.Result.Results.Single().GeneratedSources.Select(s => s.HintName).ToList();
        names.Should().OnlyHaveUniqueItems();
        names.Select(n => n.ToUpperInvariant()).Should().OnlyHaveUniqueItems("Roslyn compares hint names without case");
        names.Should().Contain(n => n.StartsWith("C_", StringComparison.Ordinal)).And.Contain(n => n.StartsWith("c_", StringComparison.Ordinal));
    }

    [Fact]
    public void Contexts_that_do_not_collide_keep_their_plain_hint_names()
    {
        var run = GeneratorHost.Run(Using + "[GenerateDeepEquals(typeof(int))] public partial class First : DeepEqualsContextBase { } [GenerateDeepEquals(typeof(int))] public partial class Second : DeepEqualsContextBase { }", load: false);
        var names = run.Result.Results.Single().GeneratedSources.Select(s => s.HintName).ToList();
        names.Should().Contain("First.g.cs").And.Contain("Second.g.cs");
    }

    [Theory]
    [InlineData("null array", "[DeepEqualsSourceGenerationOptions(ExcludeInterfacesByPrefix = null)]")]
    [InlineData("empty array", "[DeepEqualsSourceGenerationOptions(ExcludeInterfacesByPrefix = new string[0])]")]
    [InlineData("null element", "[DeepEqualsSourceGenerationOptions(ExcludeInterfacesByPrefix = new string[] { null! })]")]
    public void Null_empty_and_null_element_exclusion_prefixes_are_handled(string scenario, string attribute)
    {
        var run = GeneratorHost.Run(Using + attribute + " public partial class C : DeepEqualsContextBase { }", load: false);
        run.GeneratorDiagnosticIds.Should().NotContain("DEQ099", scenario);
        run.Result.GeneratedTrees.Should().NotBeEmpty(scenario);
    }

    [Fact]
    public void Inherited_null_exclusion_prefix_override_is_handled()
    {
        var run = GeneratorHost.Run(Using + "[DeepEqualsSourceGenerationOptions(ExcludeInterfacesByPrefix = new[] { \"System\" })] public abstract class Base : DeepEqualsContextBase { } [DeepEqualsSourceGenerationOptions(ExcludeInterfacesByPrefix = null)] public partial class C : Base { }", load: false);
        run.GeneratorDiagnosticIds.Should().NotContain("DEQ099");
        run.GeneratedSource.Should().Contain("ExcludeInterfacesByPrefix    = (none)");
    }

    [Fact]
    public void Every_generated_identifier_is_reserved()
    {
        foreach (var identifier in Analysis.Naming.IdentifiersFor("Foo"))
        {
            var run = GeneratorHost.Run(Using + $"public class Foo {{ public int Value; }} [GenerateDeepEquals(typeof(Foo))] public partial class C : DeepEqualsContextBase {{ private static int {identifier}() => 0; }}", load: false);
            run.GeneratorDiagnosticIds.Should().Contain("DEQ016", $"a user member named {identifier} collides with what the generator writes");
        }
    }

    [Fact]
    public void Framework_version_mismatch_reports_DEQ036()
    {
        GeneratorInfo.VersionOverride = "9.9.9-mismatch";
        try
        {
            var run = GeneratorHost.Run(Using + "[GenerateDeepEquals(typeof(int))] public partial class C : DeepEqualsContextBase { }", load: false);
            var error = run.GeneratorDiagnostics.Single(d => d.Id == "DEQ036");
            error.Severity.Should().Be(DiagnosticSeverity.Error);
            error.GetMessage().Should().Contain("9.9.9-mismatch");
            run.Result.GeneratedTrees.Should().BeEmpty();
        }
        finally
        {
            GeneratorInfo.VersionOverride = null;
        }
    }

    [Fact]
    public void Matching_framework_version_generates()
    {
        var run = GeneratorHost.Run(Using + "[GenerateDeepEquals(typeof(int))] public partial class C : DeepEqualsContextBase { }", load: false);
        run.GeneratorDiagnosticIds.Should().NotContain("DEQ036");
        run.Result.GeneratedTrees.Should().NotBeEmpty();
    }
}
