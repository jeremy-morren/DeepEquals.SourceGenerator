// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Linq;
using System.Net;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DeepEquals.SourceGenerator.Tests;

public sealed class SpanGateTests
{
    private const string Prelude = """
        using System;
        using System.Collections.Generic;
        using DeepEquals.SourceGeneration;
        namespace Tests;
        """;

    [Fact]
    public void Span_cores_compile_for_leaves_without_IEquatable_and_use_SequenceEqual_only_where_allowed()
    {
        var run = GeneratorHost.Run($$"""
                                      {{Prelude}}public enum Colour { Red, Green }
                                      public sealed class Arrays
                                      {
                                          public Type[]? Types;
                                          public Colour[]? Colours;
                                          public System.Net.IPAddress[]? Addresses;
                                          public int[]? Ints;
                                          public string[]? Strings;
                                          public double[]? Doubles;
                                      }

                                      [GenerateDeepEquals(typeof(Arrays))]
                                      public partial class Ctx : DeepEqualsContextBase { }
                                      """);

        run.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        run.CompileErrors.Should().BeEmpty(run.GeneratedSource);
        var source = run.GeneratedSource;
        string SpanCore(string element) => source.Substring(source.IndexOf($"private static bool Equals_SpanOf{element}(", StringComparison.Ordinal));
        SpanCore("Int32").Substring(0, 400).Should().Contain("DeepEqualsBlocks.BlockEquals<int>", "an int is its bytes");
        SpanCore("String").Substring(0, 400).Should().Contain("SequenceEqual", "string implements IEquatable<string> and is not a bit block");
        SpanCore("Type").Substring(0, 400).Should().NotContain("SequenceEqual", "Type does not implement IEquatable<Type>");
        SpanCore("Type").Substring(0, 400).Should().NotContain("BlockEquals");
        SpanCore("Colour").Substring(0, 400).Should().Contain("DeepEqualsBlocks.BlockEquals<", "an enum is the bytes of its underlying integer");
        SpanCore("IPAddress").Substring(0, 400).Should().NotContain("SequenceEqual").And.NotContain("BlockEquals");
        SpanCore("Double").Substring(0, 400).Should().Contain("DeepEqualsBlocks.BlockEquals<double>", "a bitwise leaf compares as bytes, not through double.Equals");

        var comparer = run.Comparer("Ctx", "Arrays");
        var a = run.New("Arrays"); var b = run.New("Arrays");
        var colour = run.Assembly!.GetTypes().Single(t => t.Name == "Colour");
        var colours1 = Array.CreateInstance(colour, 2); colours1.SetValue(Enum.ToObject(colour, 1), 0);
        var colours2 = Array.CreateInstance(colour, 2); colours2.SetValue(Enum.ToObject(colour, 1), 0);
        a.GetType().GetField("Types")!.SetValue(a, new[] { typeof(int), typeof(string) });
        b.GetType().GetField("Types")!.SetValue(b, new[] { typeof(int), typeof(string) });
        a.GetType().GetField("Colours")!.SetValue(a, colours1);
        b.GetType().GetField("Colours")!.SetValue(b, colours2);
        a.GetType().GetField("Addresses")!.SetValue(a, new[] { IPAddress.Loopback });
        b.GetType().GetField("Addresses")!.SetValue(b, new[] { IPAddress.Parse("127.0.0.1") });
        a.GetType().GetField("Doubles")!.SetValue(a, new[] { 0.0 });
        b.GetType().GetField("Doubles")!.SetValue(b, new[] { -0.0 });
        run.Equals(comparer, a, b).Should().BeFalse("+0 and -0 differ in a double array");
        b.GetType().GetField("Doubles")!.SetValue(b, new[] { 0.0 });
        run.Equals(comparer, a, b).Should().BeTrue();
        run.Hash(comparer, a).Should().Be(run.Hash(comparer, b));
    }
}
