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
        GeneratorRun run = GeneratorHost.Run(Prelude + """
            public enum Colour { Red, Green }
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
        string source = run.GeneratedSource;
        string SpanCore(string element) => source.Substring(source.IndexOf($"private static bool Equals_SpanOf{element}(", StringComparison.Ordinal));
        SpanCore("Int32").Substring(0, 400).Should().Contain("SequenceEqual", "int implements IEquatable<int>");
        SpanCore("String").Substring(0, 400).Should().Contain("SequenceEqual");
        SpanCore("Type").Substring(0, 400).Should().NotContain("SequenceEqual", "Type does not implement IEquatable<Type>");
        SpanCore("Colour").Substring(0, 400).Should().NotContain("SequenceEqual", "enums never implement IEquatable<T>");
        SpanCore("IPAddress").Substring(0, 400).Should().NotContain("SequenceEqual");
        SpanCore("Double").Substring(0, 400).Should().NotContain("SequenceEqual", "bitwise leaves keep the explicit loop");

        object comparer = run.Comparer("Ctx", "Arrays");
        object a = run.New("Arrays"); object b = run.New("Arrays");
        Type colour = run.Assembly!.GetTypes().Single(t => t.Name == "Colour");
        Array colours1 = Array.CreateInstance(colour, 2); colours1.SetValue(Enum.ToObject(colour, 1), 0);
        Array colours2 = Array.CreateInstance(colour, 2); colours2.SetValue(Enum.ToObject(colour, 1), 0);
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
