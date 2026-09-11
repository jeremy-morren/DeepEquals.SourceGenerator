// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using DeepEquals.SourceGeneration.Framework;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DeepEquals.SourceGenerator.Tests;

/// <summary>
/// The 64-bit hash stream: every core returns a ulong, narrow leaves pack in pairs, wide leaves are single words, the
/// public result folds, and equal values hash equal under every shape. Hash values under a fixed seed are compared
/// for the representation edges; discrimination under a random seed would be a coin toss.
/// </summary>
public sealed class Hashing64Tests
{
    private const string Prelude =
        """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using DeepEquals.SourceGeneration;
        namespace Tests;
        """;

    public const string Options64 = "[DeepEqualsSourceGenerationOptions(Hashing = DeepEqualsHashing.XxHash64)]";

    internal static GeneratorRun Clean(string source)
    {
        var run = GeneratorHost.Run(Prelude + source);
        run.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        run.CompileErrors.Should().BeEmpty(run.GeneratedSource);
        run.Assembly.Should().NotBeNull();
        return run;
    }

    /// <summary>The 64-bit seed is settable by the framework tests only; here it is reached through reflection for determinism.</summary>
    internal static IDisposable Seed64(ulong seed)
    {
        var property = typeof(DeepEqualsHashCode64).GetProperty("Seed", BindingFlags.NonPublic | BindingFlags.Static)!;
        var saved = (ulong)property.GetValue(null)!;
        property.SetValue(null, seed);
        return new Restore(() => property.SetValue(null, saved));
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    internal static void Set(object target, string member, object? value)
    {
        var type = target.GetType();
        var field = type.GetField(member);
        if (field is not null)
        {
            field.SetValue(target, value);
            return;
        }

        type.GetProperty(member)!.SetValue(target, value);
    }

    [Fact]
    public void XxHash64_cores_return_ulong_and_the_wrapper_folds()
    {
        var run = Clean($$"""
                          public sealed class Person { public string? Name; public int Age; public int[]? Scores; public List<Person>? Friends; }
                          {{Options64}}
                          [GenerateDeepEquals(typeof(Person))]
                          public partial class Ctx : DeepEqualsContextBase { }
                          """);

        var source = run.GeneratedSource;
        source.Should().Contain("private static ulong GetHashCode_Person(");
        source.Should().Contain("private static ulong ShallowHashCode_Person(", "Person is cyclic through its friends");
        source.Should().NotContain("private static int GetHashCode_");
        source.Should().Contain("DeepEqualsHashCode64.ToInt32(", "the public GetHashCode folds");
        source.Should().Contain("Hashing                      = XxHash64");

        var people = run.Comparer("Ctx", "Person");
        var a = run.New("Person"); var b = run.New("Person");
        foreach (var p in new[] { a, b })
        {
            Set(p, "Name", "n"); Set(p, "Age", 3); Set(p, "Scores", new[] { 1, 2, 3 });
        }

        run.Equals(people, a, b).Should().BeTrue();
        run.Hash(people, a).Should().Be(run.Hash(people, b));
        run.Hash(people, null).Should().Be(0, "null hashes to 0 after the fold too");
    }

    [Fact]
    public void Narrow_leaves_pack_in_pairs_before_wide_words()
    {
        var run = Clean($$"""
                          public sealed class Mixed { public int A; public double D; public int B; public int C; }
                          {{Options64}}
                          [GenerateDeepEquals(typeof(Mixed))]
                          public partial class Ctx : DeepEqualsContextBase { }
                          """);

        var source = run.GeneratedSource;
        var start = source.IndexOf("private static ulong GetHashCode_Mixed(", StringComparison.Ordinal);
        var body = source.Substring(start, source.IndexOf("\n        }", start, StringComparison.Ordinal) - start);
        body.Should().Contain("DeepEqualsHashCode64.Pack(", "the first two narrow leaves pair").And.Contain("DeepEqualsHashCode64.Narrow(", "the third narrow leaf sits alone");
        body.Should().Contain("DeepEqualsHelpers.DoubleBits(", "the double is one wide word");
        body.IndexOf("Pack(", StringComparison.Ordinal).Should().BeLessThan(body.IndexOf("Narrow(", StringComparison.Ordinal));
        body.IndexOf("Narrow(", StringComparison.Ordinal).Should().BeLessThan(body.IndexOf("DoubleBits(", StringComparison.Ordinal), "wide words follow the pairs");
        body.Should().NotContain("(uint)", "no cast is written by hand; Pack and Narrow own them");
        body.Should().NotContain("DoubleToInt64Bits");
    }

    [Fact]
    public void Equal_values_hash_equal_and_agree_with_equality_under_XxHash64()
    {
        var run = Clean($$"""
                          public struct Pair { public long A; public double B; }
                          public abstract class Shape { public string? Name; }
                          public sealed class Circle : Shape { public double Radius; }
                          public sealed class Rect : Shape { public double W; public double H; }
                          public sealed class Wide
                          {
                              public long L; public ulong UL; public double D; public decimal M; public Guid G;
                              public DateTime T; public DateTimeOffset O; public Pair P; public decimal? MaybeM; public float F;
                              public (int, string?) Tuple; public int[]? Ints; public IReadOnlyList<double>? Doubles;
                              public ImmutableArray<Guid> Guids; public HashSet<string>? Tags; public Dictionary<string, decimal>? Prices;
                              public Shape? Shape; public object? Any; public List<Wide>? Children;
                          }
                          {{Options64}}
                          [GenerateDeepEquals(typeof(Wide))]
                          [GenerateDeepEquals(typeof(Circle))]
                          [GenerateDeepEquals(typeof(Rect))]
                          public partial class Ctx : DeepEqualsContextBase { }
                          """);

        var wide = run.Comparer("Ctx", "Wide");
        object Make()
        {
            var target = run.New("Wide");
            Set(target, "L", long.MaxValue - 5); Set(target, "UL", ulong.MaxValue - 5); Set(target, "D", 1.5);
            Set(target, "M", 1.50m); Set(target, "G", new Guid("11111111-2222-3333-4444-555555555555"));
            Set(target, "T", new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            Set(target, "O", new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(3)));
            Set(target, "MaybeM", 2.5m); Set(target, "F", 0.25f);
            var pair = run.New("Pair");
            Set(pair, "A", 7L); Set(pair, "B", -0.0);
            Set(target, "P", pair);
            Set(target, "Tuple", (3, "t"));
            Set(target, "Ints", new[] { 1, 2, 3 });
            Set(target, "Doubles", new List<double> { 1.0, -0.0, double.NaN });
            Set(target, "Guids", ImmutableArray.Create(Guid.Empty, new Guid("11111111-2222-3333-4444-555555555555")));
            Set(target, "Tags", new HashSet<string> { "a", "b" });
            Set(target, "Prices", new Dictionary<string, decimal> { ["x"] = 1.0m, ["y"] = 2.00m });
            var circle = run.New("Circle");
            Set(circle, "Name", "c"); Set(circle, "Radius", 2.0);
            Set(target, "Shape", circle);
            Set(target, "Any", 42L);
            var child = run.New("Wide");
            Set(child, "L", 1L);
            var children = (System.Collections.IList)Activator.CreateInstance(target.GetType().GetField("Children")!.FieldType)!;
            children.Add(child);
            Set(target, "Children", children);
            return target;
        }

        foreach (var seed in new ulong[] { 0, 1, 0x9E3779B97F4A7C15UL, ulong.MaxValue, 12345 })
        {
            using var _ = Seed64(seed);
            var a = Make();
            var b = Make();
            run.Equals(wide, a, b).Should().BeTrue();
            run.Hash(wide, a).Should().Be(run.Hash(wide, b), $"equal values hash equal under seed {seed}");
        }
    }

    [Fact]
    public void XxHash64_distinguishes_the_representation_edges()
    {
        var run = Clean($$"""
                          public sealed class Edges { public double D; public float F; public decimal M; public DateTime T; public Guid G; public long L; }
                          {{Options64}}
                          [GenerateDeepEquals(typeof(Edges))]
                          public partial class Ctx : DeepEqualsContextBase { }
                          """);

        using var _ = Seed64(0x1234_5678_9ABC_DEF0UL);
        var edges = run.Comparer("Ctx", "Edges");
        var a = run.New("Edges");
        var b = run.New("Edges");
        void Both(string member, object value)
        {
            Set(a, member, value);
            Set(b, member, value);
        }

        Both("D", 1.0); Both("F", 1f); Both("M", 1.0m); Both("T", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)); Both("G", Guid.Empty); Both("L", 0x0000_0001_0000_0001L);
        run.Hash(edges, a).Should().Be(run.Hash(edges, b));

        Set(b, "D", -0.0); Set(a, "D", 0.0);
        run.Equals(edges, a, b).Should().BeFalse("the sign of zero is part of the bits");
        run.Hash(edges, a).Should().NotBe(run.Hash(edges, b));
        Both("D", 0.0);

        Set(a, "D", double.NaN); Set(b, "D", BitConverter.Int64BitsToDouble(0x7FF8000000000001));
        run.Equals(edges, a, b).Should().BeFalse("two NaN payloads differ");
        run.Hash(edges, a).Should().NotBe(run.Hash(edges, b));
        Both("D", 1.0);

        Set(b, "M", 1.00m);
        run.Equals(edges, a, b).Should().BeFalse("the scale is part of the value");
        run.Hash(edges, a).Should().NotBe(run.Hash(edges, b));
        Both("M", 1.0m);

        Set(b, "T", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Local));
        run.Equals(edges, a, b).Should().BeFalse("the kind is part of the value");
        run.Hash(edges, a).Should().NotBe(run.Hash(edges, b));
        Both("T", new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        Set(b, "L", 0x0000_0002_0000_0002L);
        run.Hash(edges, a).Should().NotBe(run.Hash(edges, b), "a 64-bit leaf is one word, not a folded pair");
    }

    [Fact]
    public void XxHash64_collections_use_HashSpan64_Streaming64_and_a_ulong_sum()
    {
        var run = Clean($$"""
                          public sealed class Bag { public int[]? Ints; public IReadOnlyList<string>? Names; public IEnumerable<double>? Lazy; public HashSet<int>? Set; public Dictionary<string, long>? Map; }
                          {{Options64}}
                          [GenerateDeepEquals(typeof(Bag))]
                          public partial class Ctx : DeepEqualsContextBase { }
                          """);

        var source = run.GeneratedSource;
        source.Should().Contain("DeepEqualsHashCode64.HashSpan<");
        source.Should().Contain("DeepEqualsHashCode64.Streaming h = default;");
        source.Should().Contain("ulong sum = 0;");
        source.Should().Contain("IDeepEqualsHashOps64<").And.Contain("public ulong GetHashCode64(");
        source.Should().Contain("DeepEqualsHashCode64.Fold(", "the matching fingerprint stays 32-bit");

        var bag = run.Comparer("Ctx", "Bag");
        var a = run.New("Bag"); var b = run.New("Bag");
        foreach (var target in new[] { a, b })
        {
            Set(target, "Ints", new[] { 1, 2, 3, 4, 5 });
            Set(target, "Names", new List<string> { "x", "y" });
            Set(target, "Lazy", new[] { 1.0, 2.0 }.Select(d => d));
            Set(target, "Set", new HashSet<int> { 3, 1, 2 });
            Set(target, "Map", new Dictionary<string, long> { ["a"] = 1, ["b"] = 2 });
        }

        run.Equals(bag, a, b).Should().BeTrue();
        run.Hash(bag, a).Should().Be(run.Hash(bag, b));

        var empty = run.New("Bag");
        Set(empty, "Ints", Array.Empty<int>());
        var nulls = run.New("Bag");
        run.Hash(bag, empty).Should().NotBe(run.Hash(bag, nulls), "an empty array and a null one differ");
        var ints = run.Comparer("Ctx", "ArrayOfInt32");
        run.Hash(ints, Array.Empty<int>()).Should().NotBe(0, "an empty collection never hashes to null's 0, even after the fold");
    }

    [Fact]
    public void XxHash64_custom_comparers_and_simple_types_are_narrow_words()
    {
        var run = Clean($$"""
                          public struct Money { public decimal Amount; public override int GetHashCode() => Amount.GetHashCode(); public override bool Equals(object? o) => o is Money m && m.Amount == Amount; }
                          public sealed class Label { public string? Text; }
                          public sealed class LabelComparer : IEqualityComparer<Label>
                          {
                              public bool Equals(Label? a, Label? b) => string.Equals(a?.Text, b?.Text, StringComparison.OrdinalIgnoreCase);
                              public int GetHashCode(Label o) => StringComparer.OrdinalIgnoreCase.GetHashCode(o.Text ?? "");
                          }
                          public sealed class Holder { public Money Price; public Label? Tag; public long Id; }
                          {{Options64}}
                          [GenerateDeepEquals(typeof(Holder))]
                          [SimpleType(typeof(Money))]
                          [CustomEqualityComparer(typeof(LabelComparer))]
                          public partial class Ctx : DeepEqualsContextBase { }
                          """);

        var source = run.GeneratedSource;
        var start = source.IndexOf("private static ulong GetHashCode_Holder(", StringComparison.Ordinal);
        var body = source.Substring(start, source.IndexOf("\n        }", start, StringComparison.Ordinal) - start);
        body.Should().Contain("DeepEqualsHashCode64.Pack(", "the simple type's GetHashCode and the custom comparer's pack into one word");
        body.Should().Contain(".GetHashCode()").And.Contain("_ComparerHolder.Value.GetHashCode(");

        var holder = run.Comparer("Ctx", "Holder");
        var a = run.New("Holder"); var b = run.New("Holder");
        var money = run.New("Money"); Set(money, "Amount", 1.0m);
        var la = run.New("Label"); Set(la, "Text", "abc");
        var lb = run.New("Label"); Set(lb, "Text", "ABC");
        Set(a, "Price", money); Set(b, "Price", money); Set(a, "Tag", la); Set(b, "Tag", lb); Set(a, "Id", 5L); Set(b, "Id", 5L);
        run.Equals(holder, a, b).Should().BeTrue("the custom comparer ignores case");
        run.Hash(holder, a).Should().Be(run.Hash(holder, b));
    }

#if NET7_0_OR_GREATER
    [Fact]
    public void Int128_hashes_as_two_words_under_XxHash64()
    {
        var run = Clean($$"""
                          public sealed class Big { public Int128 I; public UInt128 U; }
                          {{Options64}}
                          [GenerateDeepEquals(typeof(Big))]
                          public partial class Ctx : DeepEqualsContextBase { }
                          """);

        run.GeneratedSource.Should().Contain("DeepEqualsHashCode64.Hash(o.I)").And.Contain("DeepEqualsHashCode64.Hash(o.U)");
        using var _ = Seed64(7);
        var big = run.Comparer("Ctx", "Big");
        var a = run.New("Big"); var b = run.New("Big");
        Set(a, "I", new Int128(1, 2)); Set(b, "I", new Int128(1, 2));
        Set(a, "U", new UInt128(3, 4)); Set(b, "U", new UInt128(3, 4));
        run.Equals(big, a, b).Should().BeTrue();
        run.Hash(big, a).Should().Be(run.Hash(big, b));
        Set(b, "I", new Int128(2, 2));
        run.Equals(big, a, b).Should().BeFalse();
        run.Hash(big, a).Should().NotBe(run.Hash(big, b), "the high word takes part");
    }
#endif

    [Theory]
    [InlineData("")]
    [InlineData(Options64)]
    public void Generated_cores_contain_no_numeric_conversion(string options)
    {
        var run = Clean($$"""
                          public struct Wrapped { public double Value; public override int GetHashCode() => Value.GetHashCode(); public override bool Equals(object? o) => o is Wrapped w && w.Value.Equals(Value); }
                          public sealed class Floats
                          {
                              public float F; public double D; public Half H; public decimal M; public float? MaybeF; public double? MaybeD; public decimal? MaybeM;
                              public System.Numerics.Vector3 V; public double[]? Ds; public List<float>? Fs; public Dictionary<decimal, double>? Map; public Wrapped W;
                          }
                          {{options}}
                          [GenerateDeepEquals(typeof(Floats))]
                          [SimpleType(typeof(Wrapped))]
                          public partial class Ctx : DeepEqualsContextBase { }
                          """);

        var offences = new List<string>();
        foreach (var tree in run.Result.GeneratedTrees)
        {
            var model = run.Output.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                switch (node)
                {
                    case CastExpressionSyntax cast when IsFloatingOrDecimal(model.GetTypeInfo(cast.Expression).Type) && IsIntegral(model.GetTypeInfo(cast).Type):
                        offences.Add($"cast: {cast}");
                        break;

                    case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "GetHashCode" } access } invocation
                        when IsFloatingOrDecimal(model.GetTypeInfo(access.Expression).Type):
                        offences.Add($"GetHashCode: {invocation}");
                        break;

                    case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "GetBits" } } bits:
                        offences.Add($"GetBits: {bits}");
                        break;
                }
            }
        }

        offences.Should().BeEmpty("hashing and equality operate on storage bits, never on a numeric conversion");

        static bool IsFloatingOrDecimal(ITypeSymbol? type)
        {
            if (type is null) return false;
            if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && type is INamedTypeSymbol { TypeArguments: [var inner] })
                type = inner;

            return type.SpecialType is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal || type.Name == "Half";
        }

        static bool IsIntegral(ITypeSymbol? type) => type?.SpecialType is SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32
            or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Byte or SpecialType.System_SByte;

        // Behaviour: the edges the rule protects.
        var floats = run.Comparer("Ctx", "Floats");
        var a = run.New("Floats"); var b = run.New("Floats");
        Set(a, "D", 0.0); Set(b, "D", -0.0);
        run.Equals(floats, a, b).Should().BeFalse();
        Set(b, "D", 0.0);
        Set(a, "F", float.NaN); Set(b, "F", BitConverter.Int32BitsToSingle(0x7FC00001));
        run.Equals(floats, a, b).Should().BeFalse("two NaN payloads differ");
        Set(b, "F", float.NaN);
        run.Equals(floats, a, b).Should().BeTrue("the same NaN payload is equal to itself");
        run.Hash(floats, a).Should().Be(run.Hash(floats, b));
    }
}
