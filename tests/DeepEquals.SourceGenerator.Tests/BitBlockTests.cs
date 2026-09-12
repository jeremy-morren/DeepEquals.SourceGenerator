// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DeepEquals.SourceGenerator.Tests;

/// <summary>
/// Sequences of bit-block elements compare as one memory block and hash through XxHash3 over their bytes. The hash is
/// defined that way for every container shape of a declared type, so every shape must agree; and a struct is a bit block
/// only when its bytes are exactly its compared value.
/// </summary>
public sealed class BitBlockTests
{
    private const string Prelude =
        """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using System.Runtime.InteropServices;
        using DeepEquals.SourceGeneration;
        namespace Tests;
        """;

    private static GeneratorRun Clean(string source)
    {
        var run = GeneratorHost.Run(Prelude + source);
        run.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        run.CompileErrors.Should().BeEmpty(run.GeneratedSource);
        run.Assembly.Should().NotBeNull();
        return run;
    }

    private static string SpanCore(string source, string element)
    {
        var start = source.IndexOf($"private static bool Equals_SpanOf{element}(", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"a span core for {element} is emitted");
        return source.Substring(start, source.IndexOf("\n        }", start, StringComparison.Ordinal) - start);
    }

    [Theory]
    [InlineData("byte", "Byte", true)]
    [InlineData("sbyte", "SByte", true)]
    [InlineData("short", "Int16", true)]
    [InlineData("ushort", "UInt16", true)]
    [InlineData("char", "Char", true)]
    [InlineData("int", "Int32", true)]
    [InlineData("uint", "UInt32", true)]
    [InlineData("long", "Int64", true)]
    [InlineData("ulong", "UInt64", true)]
    [InlineData("float", "Single", true)]
    [InlineData("double", "Double", true)]
    [InlineData("Half", "Half", true)]
    [InlineData("decimal", "Decimal", true)]
    [InlineData("Guid", "Guid", true)]
    [InlineData("DateTime", "DateTime", true)]
    [InlineData("TimeSpan", "TimeSpan", true)]
    [InlineData("Int128", "Int128", true)]
    [InlineData("UInt128", "UInt128", true)]
    [InlineData("Colour", "Colour", true)]
    [InlineData("bool", "Boolean", false)]
    [InlineData("nint", "IntPtr", false)]
    [InlineData("DateTimeOffset", "DateTimeOffset", false)]
    [InlineData("string", "String", false)]
    [InlineData("Money", "Money", false)]
    [InlineData("Label", "Label", false)]
    public void Bit_block_leaves_are_classified(string type, string shortName, bool bitBlock)
    {
        var run = Clean($$"""
                                         public enum Colour : short { Red = 1, Green = 2 }
                                         public struct Money { public decimal Amount; }
                                         public struct Label { public int Code; }
                                         public sealed class LabelComparer : IEqualityComparer<Label>
                                         {
                                             public bool Equals(Label a, Label b) => a.Code == b.Code;
                                             public int GetHashCode(Label o) => o.Code;
                                         }
                                         public sealed class Holder { public {{type}}[]? Items; }
                                         [GenerateDeepEquals(typeof(Holder))]
                                         [SimpleType(typeof(Money))]
                                         [CustomEqualityComparer(typeof(LabelComparer))]
                                         public partial class Ctx : DeepEqualsContextBase { }
                                         """);

        var core = SpanCore(run.GeneratedSource, shortName);
        if (bitBlock)
            core.Should().Contain("DeepEqualsBlocks.BlockEquals<", $"{type} is its bytes");
        else
            core.Should().NotContain("BlockEquals", $"{type} is not a bit block");
    }

    [Theory]
    [InlineData("public struct S { public double X, Y, Z; }", true)]
    [InlineData("public struct S { public int A; public int B; }", true)]
    [InlineData("public struct S { public Guid G; public long L; }", true)]
    [InlineData("public struct Inner { public int A; public int B; } public struct S { public Inner I; public long L; }", true)]
    [InlineData("public readonly record struct S(double X, double Y);", true)]
    [InlineData("public struct S { public int A; public long B; }", false)]
    [InlineData("public struct S { public byte A; public int B; }", false)]
    [InlineData("public struct S { public long A; public int B; }", false)]
    [InlineData("public struct S { public int A; public string? Name; }", false)]
    [InlineData("public struct S { public int A; public bool B; public bool C; public bool D; public bool E; }", false)]
    [InlineData("[StructLayout(LayoutKind.Auto)] public struct S { public int A; public int B; }", false)]
    [InlineData("[StructLayout(LayoutKind.Explicit)] public struct S { [FieldOffset(0)] public int A; [FieldOffset(0)] public int B; }", false)]
    [InlineData("[StructLayout(LayoutKind.Sequential, Pack = 1)] public struct S { public int A; public int B; }", false)]
    [InlineData("public struct S { public int A; [DeepEqualsIgnore] public int B; }", false)]
    [InlineData("public struct S { public int A; public int? B; }", false)]
    public void Structs_without_padding_are_bit_blocks_and_padded_ones_are_not(string declaration, bool bitBlock)
    {
        var run = Clean($$"""
                                         {{declaration}}
                                         public sealed class Holder { public S[]? Items; }
                                         [GenerateDeepEquals(typeof(Holder))]
                                         public partial class Ctx : DeepEqualsContextBase { }
                                         """);

        var core = SpanCore(run.GeneratedSource, "S");
        if (bitBlock)
        {
            core.Should().Contain("if (S_IsBitBlock)").And.Contain("DeepEqualsBlocks.BlockEquals<global::Tests.S>(xs, ys)");
            run.GeneratedSource.Should().Contain("private static readonly bool S_IsBitBlock = global::DeepEquals.SourceGeneration.Framework.DeepEqualsBlocks.HasSize<global::Tests.S>(");
            core.Should().Contain("for (int i = 0;", "a struct keeps its per-element path behind the flag");
        }
        else
        {
            core.Should().NotContain("BlockEquals");
            run.GeneratedSource.Should().NotContain("S_IsBitBlock");
        }
    }

    [Fact]
    public void Nested_bit_block_structs_check_every_size()
    {
        var run = Clean("""
                                       public struct Inner { public int A; public int B; }
                                       public struct S { public Inner I; public long L; }
                                       public sealed class Holder { public S[]? Items; }
                                       [GenerateDeepEquals(typeof(Holder))]
                                       public partial class Ctx : DeepEqualsContextBase { }
                                       """);

        run.GeneratedSource.Should().Contain("HasSize<global::Tests.S>(16) && global::DeepEquals.SourceGeneration.Framework.DeepEqualsBlocks.HasSize<global::Tests.Inner>(8)");
    }

    [Fact]
    public void Bit_block_spans_compare_as_bytes_and_hash_through_XxHash3()
    {
        var run = Clean($$"""
                                         public readonly record struct Point3(double X, double Y, double Z);
                                         public sealed class Holder
                                         {
                                             public double[]? Doubles; public List<Guid>? Guids; public ImmutableArray<decimal> Decimals;
                                             public IReadOnlyList<float>? Floats; public IEnumerable<Point3>? Points; public IList<long>? Longs;
                                             public string[]? Strings; public bool[]? Flags; public List<int?>? Maybe; public Memory<int> Memory;
                                         }
                                         [GenerateDeepEquals(typeof(Holder))]
                                         public partial class Ctx : DeepEqualsContextBase { }
                                         """);

        var source = run.GeneratedSource;
        source.Should().Contain($"DeepEqualsBlocks.HashBlock<double>(o)", "an array hashes its own span");
        source.Should().Contain($"DeepEqualsBlocks.HashBlock<global::System.Guid>(global::System.Runtime.InteropServices.CollectionsMarshal.AsSpan(o))");
        source.Should().Contain($"DeepEqualsBlocks.HashBlock<decimal>(o.AsSpan())");
        source.Should().Contain($"DeepEqualsBlocks.HashReadOnlyList<float>(");
        source.Should().Contain($"DeepEqualsBlocks.HashList<long>(");
        source.Should().Contain($"DeepEqualsBlocks.HashEnumerable<global::Tests.Point3>(");
        source.Should().Contain($"DeepEqualsBlocks.HashBlock<int>(o.Span)");
        Regex.IsMatch(source, @"DeepEqualsBlocks\.\w+<(string|bool|int\?)>").Should().BeFalse("strings, bools and nullables are not bit blocks");
    }

    /// <summary>An IReadOnlyList that is neither an array nor a List.</summary>
    public sealed class Custom<T>(T[] items) : IReadOnlyList<T>
    {
        public T this[int index] => items[index];
        public int Count => items.Length;
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)items).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void Every_container_shape_of_a_bit_block_sequence_hashes_equal()
    {
        var run = Clean($$"""
                                         public readonly record struct Point3(double X, double Y, double Z);
                                         public sealed class Holder
                                         {
                                             public IReadOnlyList<double>? List; public IEnumerable<double>? Sequence;
                                             public IReadOnlyList<Point3>? Points; public IEnumerable<Point3>? PointSequence;
                                         }
                                         [GenerateDeepEquals(typeof(Holder))]
                                         public partial class Ctx : DeepEqualsContextBase { }
                                         """);

        var doubles = new[] { 1.0, -0.0, double.NaN, 2.5, double.MaxValue };
        var point = run.Assembly!.GetTypes().Single(t => t.Name == "Point3");
        var points = Array.CreateInstance(point, 3);
        for (var i = 0; i < 3; i++)
            points.SetValue(Activator.CreateInstance(point, i * 1.0, -i * 1.0, 0.5), i);

        object[] Shapes(Array items)
        {
            var element = items.GetType().GetElementType()!;
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(element), items)!;
            var custom = Activator.CreateInstance(typeof(Custom<>).MakeGenericType(element), items)!;
            var immutable = typeof(ImmutableArray).GetMethods().Single(m => m.Name == "CreateRange" && m.GetParameters().Length == 1)
                .MakeGenericMethod(element).Invoke(null, [items])!;
            var lazy = typeof(Enumerable).GetMethod(nameof(Enumerable.Skip))!.MakeGenericMethod(element).Invoke(null, [items, 0])!;
            return [items, list, custom, immutable, lazy];
        }

        void Check(string comparerName, Array items, bool enumerableToo)
        {
            var comparer = run.Comparer("Ctx", comparerName);
            var shapes = Shapes(items).Take(enumerableToo ? 5 : 4).ToArray();
            var expected = run.Hash(comparer, shapes[0]);
            foreach (var shape in shapes)
            {
                run.Hash(comparer, shape).Should().Be(expected, $"{comparerName} over {shape.GetType().Name} hashes like the array");
                run.Equals(comparer, shapes[0], shape).Should().BeTrue($"{comparerName} over {shape.GetType().Name} equals the array");
            }
        }

        Check("IReadOnlyListOfDouble", doubles, enumerableToo: false);
        Check("IEnumerableOfDouble", doubles, enumerableToo: true);
        Check("IReadOnlyListOfPoint3", points, enumerableToo: false);
        Check("IEnumerableOfPoint3", points, enumerableToo: true);
    }

    [Fact]
    public void Bit_block_sequence_equality_is_bitwise()
    {
        var run = Clean($$"""
                                         public sealed class Holder { public double[]? Doubles; public decimal[]? Decimals; }
                                         [GenerateDeepEquals(typeof(Holder))]
                                         public partial class Ctx : DeepEqualsContextBase { }
                                         """);

        var arrays = run.Comparer("Ctx", "ArrayOfDouble");
        run.Equals(arrays, new[] { 0.0 }, new[] { -0.0 }).Should().BeFalse("the sign of zero is a bit");
        run.Equals(arrays, new[] { double.NaN }, new[] { double.NaN }).Should().BeTrue("the same NaN payload is the same bits");
        run.Equals(arrays, new[] { double.NaN }, new[] { BitConverter.Int64BitsToDouble(0x7FF8000000000001) }).Should().BeFalse("NaN payloads differ");
        run.Hash(arrays, new[] { 1.0, 2.0 }).Should().Be(run.Hash(arrays, new[] { 1.0, 2.0 }));
        run.Hash(arrays, Array.Empty<double>()).Should().NotBe(0, "an empty array never hashes like null");
        run.Hash(arrays, null).Should().Be(0);

        var decimals = run.Comparer("Ctx", "ArrayOfDecimal");
        decimal scale1 = 1.5m, scale2 = 1.50m;
        run.Equals(decimals, new[] { scale1 }, new[] { scale2 }).Should().BeFalse("the scale is part of the value");
    }

    [Fact]
    public void Size_check_failure_falls_back_to_the_word_path()
    {
        const string source = """
                              public struct Pair { public int A; public int B; }
                              public sealed class Holder { public Pair[]? Items; public IEnumerable<Pair>? Lazy; }
                              [GenerateDeepEquals(typeof(Holder))]
                              public partial class Ctx : DeepEqualsContextBase { }
                              """;
        var run = Clean(source);
        run.GeneratedSource.Should().Contain("HasSize<global::Tests.Pair>(8)");

        // Rewrite the generated flag to a size the runtime will never report, then compile and load that instead.
        var output = run.Output;
        foreach (var tree in output.SyntaxTrees.Where(t => t.ToString().Contains("Pair_IsBitBlock =", StringComparison.Ordinal)).ToList())
        {
            var text = tree.ToString().Replace("HasSize<global::Tests.Pair>(8)", "HasSize<global::Tests.Pair>(9)");
            output = output.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(text, (CSharpParseOptions)tree.Options, tree.FilePath));
        }

        using var stream = new MemoryStream();
        var emit = output.Emit(stream);
        emit.Success.Should().BeTrue(string.Join("\n", emit.Diagnostics));
        var assembly = Assembly.Load(stream.ToArray());
        var context = assembly.GetTypes().Single(t => t.Name == "Ctx");
        context.GetField("Pair_IsBitBlock", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null).Should().Be(false);

        var pairType = assembly.GetTypes().Single(t => t.Name == "Pair");
        Array Pairs(params int[] values)
        {
            var array = Array.CreateInstance(pairType, values.Length);
            for (var i = 0; i < values.Length; i++)
            {
                var pair = Activator.CreateInstance(pairType)!;
                pairType.GetField("A")!.SetValue(pair, values[i]);
                pairType.GetField("B")!.SetValue(pair, -values[i]);
                array.SetValue(pair, i);
            }

            return array;
        }

        object Comparer(string name) => context.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var arrays = Comparer("ArrayOfPair");
        var sequences = Comparer("IEnumerableOfPair");
        bool Equal(object comparer, object? x, object? y) => (bool)comparer.GetType().GetMethod("Equals", [x!.GetType().IsArray && comparer == arrays ? x.GetType() : typeof(IEnumerable<>).MakeGenericType(pairType), comparer == arrays ? x.GetType() : typeof(IEnumerable<>).MakeGenericType(pairType)])!.Invoke(comparer, [x, y])!;
        int Hash(object comparer, object? value) => (int)comparer.GetType().GetMethod("GetHashCode", [comparer == arrays ? value!.GetType() : typeof(IEnumerable<>).MakeGenericType(pairType)])!.Invoke(comparer, [value])!;

        Equal(arrays, Pairs(1, 2, 3), Pairs(1, 2, 3)).Should().BeTrue("the per-element path still compares");
        Equal(arrays, Pairs(1, 2, 3), Pairs(1, 2, 4)).Should().BeFalse();
        Hash(arrays, Pairs(1, 2, 3)).Should().Be(Hash(arrays, Pairs(1, 2, 3)));

        var lazy = typeof(Enumerable).GetMethod(nameof(Enumerable.Skip))!.MakeGenericMethod(pairType).Invoke(null, [Pairs(1, 2, 3), 0])!;
        Hash(sequences, Pairs(1, 2, 3)).Should().Be(Hash(sequences, lazy), "with the flag false every shape takes the per-element path, and they still agree");
        Equal(sequences, Pairs(1, 2, 3), lazy).Should().BeTrue();

        Regex.IsMatch(run.GeneratedSource, @"if \(Pair_IsBitBlock\)").Should().BeTrue();
    }
}
