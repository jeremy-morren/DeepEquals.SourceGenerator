// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DeepEquals.SourceGenerator.Tests;

/// <summary>
/// Obsolete, experimental and preview APIs in the compared types: generated code uses them without a warning the
/// consuming project did not already accept, never calls what is obsolete as an error, and passes an obsolete type's
/// status on to its comparer. The header lists every suppression on its own line.
/// </summary>
public sealed class ObsoleteTests
{
    private const string Prelude =
        """
        using System;
        using System.Collections.Generic;
        using DeepEquals.SourceGeneration;
        namespace Tests;
        """;

    private static GeneratorRun Run(string source)
    {
        var run = GeneratorHost.Run(Prelude + source);
        run.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        return run;
    }

    /// <summary>Warnings and errors the compiler reports inside generated files: there must be none.</summary>
    private static List<Diagnostic> InGenerated(GeneratorRun run)
    {
        var generated = new HashSet<SyntaxTree>(run.Result.GeneratedTrees);
        return run.Output.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Warning && d.Location.SourceTree is { } tree && generated.Contains(tree))
            .ToList();
    }

    /// <summary>Diagnostics the compiler reports in the test's own source.</summary>
    private static List<string> InSource(GeneratorRun run) =>
        run.Output.GetDiagnostics()
            .Where(d => d.Location.SourceTree?.FilePath == "Input.cs" && d.Severity >= DiagnosticSeverity.Warning)
            .Select(d => d.Id)
            .ToList();

    private static string File(GeneratorRun run, string hintName) =>
        run.Result.Results.Single().GeneratedSources.Single(s => s.HintName == hintName).SourceText.ToString().Replace("\r\n", "\n");

    private static void Set(object target, string member, object? value)
    {
        var type = target.GetType();
        var field = type.GetField(member, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (field is not null)
        {
            field.SetValue(target, value);
            return;
        }

        type.GetField($"<{member}>k__BackingField", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.SetValue(target, value);
    }

    [Fact]
    public void Header_lists_each_suppression_on_its_own_line_and_the_user_code_ones_after_a_blank_line()
    {
        var run = Run("""
                      #pragma warning disable EXP001
                      [System.Diagnostics.CodeAnalysis.Experimental("EXP001")]
                      public sealed class Trial { public int X; }
                      public sealed class Holder { public Trial? T; public int N; }

                      [GenerateDeepEquals(typeof(Holder))]
                      public partial class Ctx : DeepEqualsContextBase { }
                      #pragma warning restore EXP001
                      """);

        InGenerated(run).Should().BeEmpty();
        var file = File(run, "Tests.Ctx.Holder.g.cs");
        var lines = file.Split('\n');
        var pragmas = lines.Select((line, index) => (line, index)).Where(x => x.line.StartsWith("#pragma warning disable", StringComparison.Ordinal)).ToList();

        pragmas.Should().OnlyContain(p => Regex.IsMatch(p.line, @"^#pragma warning disable [A-Z]+[0-9]+ // Suppress "), "one warning per line, each with its reason");
        pragmas.Select(p => p.line).Should().Contain(l => l.StartsWith("#pragma warning disable CS8632 // Suppress the warning for nullable annotations", StringComparison.Ordinal));

        // The generator's own group, a blank line, then the warnings the compared types' APIs raise.
        var own = pragmas.FindIndex(p => p.line.Contains(" CS8632 "));
        var user = pragmas.FindIndex(p => p.line.Contains(" CS0612 "));
        user.Should().BeGreaterThan(own);
        lines[pragmas[user].index - 1].Should().BeEmpty("a blank line separates the two groups");
        pragmas.Select(p => p.line).Should().Contain("#pragma warning disable EXP001 // Suppress the EXP001 warning for [Experimental] on Tests.Trial");
        pragmas.FindIndex(p => p.line.Contains(" EXP001 ")).Should().BeGreaterThan(user, "a user-code suppression belongs to the second group");
    }

    [Fact]
    public void An_obsolete_type_marks_its_comparer_and_convenience_property()
    {
        var run = Run("""
                      [Obsolete("Use Current")]
                      public sealed class Legacy { public int X { get; set; } }

                      #pragma warning disable CS0618
                      [GenerateDeepEquals(typeof(Legacy))]
                      [GenerateDeepEquals(typeof(List<Legacy>))]
                      #pragma warning restore CS0618
                      public partial class Ctx : DeepEqualsContextBase { }

                      public static class Consumer
                      {
                          public static object Use() => Ctx.Legacy;
                      }
                      """);

        run.CompileErrors.Should().BeEmpty();
        InGenerated(run).Should().BeEmpty("generated code uses the obsolete type under the header's suppressions");

        var file = File(run, "Tests.Ctx.Legacy.g.cs");
        file.Should().Contain("[global::System.Obsolete(\"Use Current\")]\n        public static LegacyEqualityComparer Legacy =>");
        file.Should().Contain("[global::System.Obsolete(\"Use Current\")]\n        [global::System.CodeDom.Compiler.GeneratedCode(");
        File(run, "Tests.Ctx.ListOfLegacy.g.cs").Should().Contain("[global::System.Obsolete(\"Use Current\")]",
            "a name built from an obsolete type argument uses it too");
        File(run, "Tests.Ctx.Int32.g.cs").Should().NotContain("[global::System.Obsolete");

        InSource(run).Should().Contain("CS0618", "a caller of the comparer sees the type's own warning");

        var comparer = run.Comparer("Ctx", "Legacy");
        var a = run.New("Legacy"); var b = run.New("Legacy");
        Set(a, "X", 1); Set(b, "X", 1);
        run.Equals(comparer, a, b).Should().BeTrue();
        Set(b, "X", 2);
        run.Equals(comparer, a, b).Should().BeFalse();
    }

    [Fact]
    public void A_custom_diagnostic_id_and_url_are_copied_and_suppressed()
    {
        var run = Run("""
                      [Obsolete("Retired", DiagnosticId = "OLD001", UrlFormat = "https://example.com/{0}")]
                      public sealed class Retired { public int X; }

                      #pragma warning disable OLD001
                      [GenerateDeepEquals(typeof(Retired))]
                      #pragma warning restore OLD001
                      public partial class Ctx : DeepEqualsContextBase { }

                      public static class Consumer
                      {
                          public static object Use() => Ctx.Retired;
                      }
                      """);

        run.CompileErrors.Should().BeEmpty();
        InGenerated(run).Should().BeEmpty();
        var file = File(run, "Tests.Ctx.Retired.g.cs");
        file.Should().Contain("[global::System.Obsolete(\"Retired\", DiagnosticId = \"OLD001\", UrlFormat = \"https://example.com/{0}\")]");
        file.Should().Contain("#pragma warning disable OLD001 // Suppress the OLD001 warning for [Obsolete] on Tests.Retired");
        InSource(run).Should().Contain("OLD001", "the custom id reaches the caller, as the type's own would");
    }

    [Fact]
    public void Obsolete_members_are_suppressed_and_error_level_ones_are_read_through_their_storage()
    {
        var run = Run("""
                      public sealed class Holder
                      {
                          public int Keep { get; set; }
                          [Obsolete("soft")] public int Soft { get; set; }
                          [Obsolete("hard", true)] public int Hard { get; set; }
                          [Obsolete("field", true)] public int Field;
                      }

                      [GenerateDeepEquals(typeof(Holder))]
                      public partial class Ctx : DeepEqualsContextBase { }
                      """);

        run.CompileErrors.Should().BeEmpty("an error-level obsolete member is never named in generated code");
        InGenerated(run).Should().BeEmpty("a warning-level one is named under the header's suppressions");
        var source = run.GeneratedSource;
        source.Should().Contain("x.Soft == y.Soft", "a warning-level member keeps its getter");
        source.Should().NotContain("x.Hard").And.NotContain("x.Field");
        source.Should().Contain("Name = \"<Hard>k__BackingField\"").And.Contain("Name = \"Field\"");

        var comparer = run.Comparer("Ctx", "Holder");
        var a = run.New("Holder"); var b = run.New("Holder");
        run.Equals(comparer, a, b).Should().BeTrue();
        Set(b, "Hard", 5);
        run.Equals(comparer, a, b).Should().BeFalse("the error-level property's storage is compared");
        Set(b, "Hard", 0); Set(b, "Field", 7);
        run.Equals(comparer, a, b).Should().BeFalse("so is the error-level field");
        run.Hash(comparer, b).Should().NotBe(run.Hash(comparer, a));
    }

    [Fact]
    public void An_error_level_obsolete_type_compiles_inside_an_obsolete_context()
    {
        var run = Run("""
                      [Obsolete("Gone", true)]
                      public sealed class Gone { public int X { get; set; } }

                      [Obsolete("The legacy comparers")]
                      [GenerateDeepEquals(typeof(Gone))]
                      public partial class Ctx : DeepEqualsContextBase { }
                      """);

        run.GeneratorDiagnosticIds.Should().NotContain("DEQ038", "every generated member is inside the obsolete context");
        run.CompileErrors.Should().BeEmpty();
        File(run, "Tests.Ctx.Gone.g.cs").Should().Contain("[global::System.Obsolete(\"Gone\", true)]\n        [global::System.CodeDom.Compiler.GeneratedCode(");

        var comparer = run.Comparer("Ctx", "Gone");
        var a = run.New("Gone"); var b = run.New("Gone");
        run.Equals(comparer, a, b).Should().BeTrue();
        Set(b, "X", 3);
        run.Equals(comparer, a, b).Should().BeFalse();
    }

    [Fact]
    public void An_error_level_obsolete_type_outside_an_obsolete_context_reports_DEQ038()
    {
        var run = GeneratorHost.Run(Prelude + """
                                              [Obsolete("Gone", true)]
                                              public sealed class Gone { public int X; }

                                              [Obsolete("legacy")]
                                              public sealed class Legacy { public Gone? Inner; }

                                              #pragma warning disable CS0618
                                              [GenerateDeepEquals(typeof(Legacy))]
                                              #pragma warning restore CS0618
                                              public partial class Ctx : DeepEqualsContextBase { }
                                              """, load: false);

        var error = run.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "DEQ038").Subject;
        error.Severity.Should().Be(DiagnosticSeverity.Error);
        error.GetMessage().Should().Contain("Tests.Gone").And.Contain("[Obsolete]");
        run.Result.GeneratedTrees.Should().BeEmpty("a context that cannot compile is not emitted");
    }
}
