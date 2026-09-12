// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using Xunit.Abstractions;

namespace DeepEquals.SourceGenerator.Tests;

public sealed class StrategyAndDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    public StrategyAndDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private const string Prelude = """
        using System;
        using System.Collections.Generic;
        using DeepEquals.SourceGeneration;
        namespace Tests;
        """;

    private GeneratorRun Clean(string source, params string[] expectedWarnings)
    {
        var run = GeneratorHost.Run(Prelude + source);
        if (run.CompileErrors.Any() || run.GeneratorDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            _output.WriteLine(run.GeneratedSource);
            _output.WriteLine(string.Join(Environment.NewLine, run.CompileErrors.Concat(run.GeneratorDiagnostics.Select(d => d.ToString()))));
        }

        run.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        run.CompileErrors.Should().BeEmpty();
        run.Assembly.Should().NotBeNull();
        foreach (var id in expectedWarnings) run.GeneratorDiagnosticIds.Should().Contain(id);

        return run;
    }

    private static GeneratorRun Diagnose(string source) => GeneratorHost.Run(Prelude + source, load: false);

    private static void Set(object target, string member, object? value)
    {
        var type = target.GetType();
        var field = type.GetField(member, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (field is not null)
        {
            field.SetValue(target, value);
            return;
        }

        type.GetProperty(member)!.SetValue(target, value);
    }

    [Fact]
    public void Custom_comparer_replaces_comparison_of_its_type_including_collection_targets()
    {
        var run = Clean("""
                        public sealed class Money { public decimal Amount; public string? Currency; }
                        public sealed class MoneyComparer : IEqualityComparer<Money>
                        {
                            public static readonly MoneyComparer Instance = new MoneyComparer();
                            public bool Equals(Money? x, Money? y) => x!.Amount == y!.Amount;   // ignores currency and scale
                            public int GetHashCode(Money o) => o.Amount.GetHashCode();
                        }
                        public sealed class Order
                        {
                            public Money? Total;
                            public Dictionary<string, int>? Attributes;
                            public string? Note;
                        }

                        [GenerateDeepEquals(typeof(Order))]
                        [CustomEqualityComparer(typeof(MoneyComparer))]
                        [CustomEqualityComparer(typeof(StringComparer), nameof(StringComparer.OrdinalIgnoreCase))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        var comparer = run.Comparer("Ctx", "Order");
        var a = run.New("Order"); var b = run.New("Order");
        var m1 = run.New("Money"); Set(m1, "Amount", 1.0m); Set(m1, "Currency", "USD");
        var m2 = run.New("Money"); Set(m2, "Amount", 1.00m); Set(m2, "Currency", "EUR");
        Set(a, "Total", m1); Set(b, "Total", m2);
        Set(a, "Note", "Hello"); Set(b, "Note", "HELLO");
        Set(a, "Attributes", new Dictionary<string, int> { ["k"] = 1 });
        Set(b, "Attributes", new Dictionary<string, int> { ["K"] = 1 });
        run.Equals(comparer, a, b).Should().BeTrue("the custom comparers own Money and string, dictionary keys included");
        run.Hash(comparer, a).Should().Be(run.Hash(comparer, b));
        Set(b, "Note", "other");
        run.Equals(comparer, a, b).Should().BeFalse();
        Set(a, "Total", null);
        run.Equals(comparer, a, b).Should().BeFalse("the library null rule applies before the comparer");
    }

    [Fact]
    public void Custom_comparer_with_handleNulls_owns_null()
    {
        var run = Clean("""
                        public sealed class Label { public string? Text; }
                        public sealed class LabelComparer : IEqualityComparer<Label?>
                        {
                            public bool Equals(Label? x, Label? y) => (x?.Text ?? "") == (y?.Text ?? "");
                            public int GetHashCode(Label? o) => (o?.Text ?? "").Length;
                        }
                        public sealed class Card { public Label? Front; }

                        [GenerateDeepEquals(typeof(Card))]
                        [CustomEqualityComparer(typeof(LabelComparer), handleNulls: true)]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        var comparer = run.Comparer("Ctx", "Card");
        var a = run.New("Card"); var b = run.New("Card");
        var empty = run.New("Label"); Set(empty, "Text", "");
        Set(a, "Front", null); Set(b, "Front", empty);
        run.Equals(comparer, a, b).Should().BeTrue("the comparer decided that null equals an empty label");
        run.Hash(comparer, a).Should().Be(run.Hash(comparer, b));
    }

    [Fact]
    public void Simple_type_is_a_leaf_compared_with_default_equality()
    {
        var run = Clean("""
                        public sealed class Sku : IEquatable<Sku>
                        {
                            public string? Code; public int Ignored;
                            public bool Equals(Sku? other) => other is not null && Code == other.Code;
                            public override bool Equals(object? o) => Equals(o as Sku);
                            public override int GetHashCode() => Code?.GetHashCode() ?? 0;
                        }
                        public sealed class Item { public Sku? Sku; public List<Sku>? Alternatives; }

                        [GenerateDeepEquals(typeof(Item))]
                        [SimpleType(typeof(Sku))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        var comparer = run.Comparer("Ctx", "Item");
        var a = run.New("Item"); var b = run.New("Item");
        var s1 = run.New("Sku"); Set(s1, "Code", "X"); Set(s1, "Ignored", 1);
        var s2 = run.New("Sku"); Set(s2, "Code", "X"); Set(s2, "Ignored", 2);
        Set(a, "Sku", s1); Set(b, "Sku", s2);
        run.Equals(comparer, a, b).Should().BeTrue("Sku uses its own Equals, which ignores the second field");
        run.Hash(comparer, a).Should().Be(run.Hash(comparer, b));
        run.GeneratedSource.Should().Contain(".Equals(", "a sealed class with a public Equals(Sku) is called directly");
        run.GeneratedSource.Should().NotContain("EqualityComparer<global::Tests.Sku>.Default");
    }

    [Fact]
    public void Custom_comparer_coverage_ignores_user_defined_implicit_conversions()
    {
        // JsonNode declares implicit operators from every primitive; a comparer registered for it must not capture them.
        var run = Clean("""
                        public sealed class JsonLike
                        {
                            public string? Text;
                            public static implicit operator JsonLike(uint value) => new JsonLike { Text = value.ToString() };
                            public static implicit operator JsonLike(string? value) => new JsonLike { Text = value };
                            public static implicit operator JsonLike?(decimal? value) => value is null ? null : new JsonLike { Text = value.Value.ToString() };
                        }
                        public sealed class JsonLikeComparer : IEqualityComparer<JsonLike>
                        {
                            public static int Calls;
                            public bool Equals(JsonLike? a, JsonLike? b) { Calls++; return string.Equals(a?.Text, b?.Text, StringComparison.OrdinalIgnoreCase); }
                            public int GetHashCode(JsonLike o) => StringComparer.OrdinalIgnoreCase.GetHashCode(o.Text ?? "");
                        }
                        public sealed class Holder { public uint Position; public string? Name; public decimal? Amount; public JsonLike? Node; }

                        [GenerateDeepEquals(typeof(Holder))]
                        [CustomEqualityComparer(typeof(JsonLikeComparer))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        var comparer = run.Comparer("Ctx", "Holder");
        var a = run.New("Holder"); var b = run.New("Holder");
        Set(a, "Position", 1u); Set(b, "Position", 1u);
        Set(a, "Name", "abc"); Set(b, "Name", "ABC");
        run.Equals(comparer, a, b).Should().BeFalse("strings stay ordinal instead of going through the JSON comparer");
        Set(b, "Name", "abc");
        Set(a, "Amount", 1.0m); Set(b, "Amount", 1.00m);
        run.Equals(comparer, a, b).Should().BeFalse("decimal keeps its exact-representation rule");
        Set(b, "Amount", 1.0m);
        var n1 = run.New("JsonLike"); Set(n1, "Text", "x"); var n2 = run.New("JsonLike"); Set(n2, "Text", "X");
        Set(a, "Node", n1); Set(b, "Node", n2);
        run.Equals(comparer, a, b).Should().BeTrue("only the JsonLike member uses the custom comparer");
        run.Hash(comparer, a).Should().Be(run.Hash(comparer, b));
        Set(b, "Position", 2u);
        run.Equals(comparer, a, b).Should().BeFalse();
        var calls = (int)run.Assembly!.GetTypes().Single(t => t.Name == "JsonLikeComparer").GetField("Calls")!.GetValue(null)!;
        calls.Should().Be(1, "the comparer runs once per comparison that reaches the JsonLike member, never for primitives");
    }

    [Fact]
    public void Custom_comparer_coverage_ignores_implicit_numeric_conversions()
    {
        var run = Clean("""
                        public sealed class LongComparer : IEqualityComparer<long>
                        {
                            public bool Equals(long a, long b) => a / 10 == b / 10;
                            public int GetHashCode(long o) => (o / 10).GetHashCode();
                        }
                        public sealed class IntComparer : IEqualityComparer<int>
                        {
                            public bool Equals(int a, int b) => a == b;
                            public int GetHashCode(int o) => o;
                        }
                        public sealed class Holder { public int I; public long L; public short S; public double D; }

                        [GenerateDeepEquals(typeof(Holder))]
                        [CustomEqualityComparer(typeof(LongComparer))]
                        [CustomEqualityComparer(typeof(IntComparer))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        run.GeneratorDiagnosticIds.Should().NotContain("DEQ030", "int and long are unrelated types, not an overlap");
        run.GeneratorDiagnosticIds.Should().NotContain("DEQ031");
        var comparer = run.Comparer("Ctx", "Holder");
        var a = run.New("Holder"); var b = run.New("Holder");
        Set(a, "L", 21L); Set(b, "L", 29L);
        run.Equals(comparer, a, b).Should().BeTrue("the long comparer buckets by tens");
        Set(a, "S", (short)21); Set(b, "S", (short)29);
        run.Equals(comparer, a, b).Should().BeFalse("short is not covered by the long comparer");
        Set(b, "S", (short)21); Set(a, "I", 21); Set(b, "I", 29);
        run.Equals(comparer, a, b).Should().BeFalse("int has its own exact comparer, not the long one");
    }

    [Fact]
    public void Simple_type_coverage_ignores_user_defined_implicit_conversions()
    {
        var run = Clean("""
                        public sealed class Wrapper
                        {
                            public decimal V;
                            public override bool Equals(object? o) => o is Wrapper w && w.V == V;
                            public override int GetHashCode() => V.GetHashCode();
                        }
                        public struct Money
                        {
                            public decimal Amount; public string? Currency;
                            public static implicit operator Wrapper(Money m) => new Wrapper { V = m.Amount };
                        }
                        public sealed class Holder { public Money M; public Wrapper? W; }

                        [GenerateDeepEquals(typeof(Holder))]
                        [SimpleType(typeof(Wrapper))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        var source = run.GeneratedSource;
        source.Should().Contain("private static bool Equals_Money(", "Money is a struct compared by members");
        source.Should().NotContain("Equals_Wrapper(", "Wrapper is a [SimpleType] leaf with no core of its own");

        var comparer = run.Comparer("Ctx", "Holder");
        var a = run.New("Holder"); var b = run.New("Holder");
        var m1 = run.New("Money"); Set(m1, "Amount", 1m); Set(m1, "Currency", "GBP");
        var m2 = run.New("Money"); Set(m2, "Amount", 1m); Set(m2, "Currency", "USD");
        Set(a, "M", m1); Set(b, "M", m2);
        run.Equals(comparer, a, b).Should().BeFalse("Money is walked by members, so Currency counts; the Wrapper conversion is not coverage");
    }

    [Fact]
    public void Simple_types_implementing_IEquatable_of_self_bypass_the_default_comparer()
    {
        var run = Clean("""
                        public interface IStringId { string Value { get; } }
                        public record struct Id1(string Value) : IStringId;
                        public readonly struct Id2 : IEquatable<Id2>, IStringId
                        {
                            public Id2(string value) { Value = value; }
                            public string Value { get; }
                            public bool Equals(Id2 other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
                            public override bool Equals(object? o) => o is Id2 other && Equals(other);
                            public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value ?? "");
                        }
                        public struct Id3 : IEquatable<Id3>, IStringId
                        {
                            public Id3(string value) { Value = value; }
                            public string Value { get; }
                            bool IEquatable<Id3>.Equals(Id3 other) => string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
                            public override bool Equals(object? o) => o is Id3 other && ((IEquatable<Id3>)this).Equals(other);
                            public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value ?? "");
                        }
                        public class Base : IEquatable<Base>
                        {
                            public string? Key;
                            public bool Equals(Base? other) => other is not null && string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase);
                            public override bool Equals(object? o) => Equals(o as Base);
                            public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Key ?? "");
                        }
                        public sealed class Derived : Base { }
                        public sealed class Holder
                        {
                            public Id1 A; public Id2 B; public Id3 C; public Id2? D; public IStringId? E; public Base? F; public Derived? G; public List<Id2>? H;
                        }

                        [GenerateDeepEquals(typeof(Holder))]
                        [SimpleType(typeof(IStringId))]
                        [SimpleType(typeof(Base))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        var source = run.GeneratedSource;
        // A record struct's synthesized Equals(Id1) and Id2's public Equals(Id2) are called directly.
        source.Should().NotContain("EqualityComparer<global::Tests.Id1>.Default");
        source.Should().NotContain("EqualityComparer<global::Tests.Id2>.Default");
        // An explicit implementation on a struct goes through the constrained shim so it never boxes.
        source.Should().Contain("EquatableEquals<global::Tests.Id3>(");
        source.Should().NotContain("EqualityComparer<global::Tests.Id3>.Default");
        // The interface registration only decides coverage; a member of the interface type keeps the default comparer.
        source.Should().Contain("EqualityComparer<global::Tests.IStringId>.Default.Equals(");
        // An unsealed Base : IEquatable<Base> takes the shim (interface dispatch, as the default comparer does);
        // Derived only inherits IEquatable<Base>, which is not an exact match, so it keeps the default comparer.
        source.Should().Contain("EquatableEquals<global::Tests.Base>(");
        source.Should().Contain("EqualityComparer<global::Tests.Derived>.Default.Equals(");
        source.Should().NotContain("EqualityComparer<global::Tests.Base>.Default");

        var comparer = run.Comparer("Ctx", "Holder");
        var a = run.New("Holder"); var b = run.New("Holder");
        Set(a, "A", run.New("Id1", "x")); Set(b, "A", run.New("Id1", "x"));
        Set(a, "B", run.New("Id2", "abc")); Set(b, "B", run.New("Id2", "ABC"));
        Set(a, "C", run.New("Id3", "abc")); Set(b, "C", run.New("Id3", "ABC"));
        Set(a, "D", run.New("Id2", "q")); Set(b, "D", run.New("Id2", "Q"));
        Set(a, "E", run.New("Id1", "e")); Set(b, "E", run.New("Id1", "e"));
        var f1 = run.New("Base"); Set(f1, "Key", "k"); var f2 = run.New("Base"); Set(f2, "Key", "K");
        Set(a, "F", f1); Set(b, "F", f2);
        var g1 = run.New("Derived"); Set(g1, "Key", "d"); var g2 = run.New("Derived"); Set(g2, "Key", "D");
        Set(a, "G", g1); Set(b, "G", g2);
        var id2 = run.Assembly!.GetTypes().Single(t => t.Name == "Id2");
        var h1 = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(id2))!;
        var h2 = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(id2))!;
        h1.Add(run.New("Id2", "one")); h2.Add(run.New("Id2", "ONE"));
        Set(a, "H", h1); Set(b, "H", h2);

        run.Equals(comparer, a, b).Should().BeTrue("every leaf's own case-insensitive Equals decides");
        run.Hash(comparer, a).Should().Be(run.Hash(comparer, b));
        Set(b, "A", run.New("Id1", "y"));
        run.Equals(comparer, a, b).Should().BeFalse();
        Set(b, "A", run.New("Id1", "x")); Set(b, "C", run.New("Id3", "other"));
        run.Equals(comparer, a, b).Should().BeFalse();
    }

    [Fact]
    public void Context_level_ignore_excludes_storage_by_name_and_records_and_primary_constructors_work()
    {
        var run = Clean("""
                        public class Base { private int _cache; public int Visible; public void Touch(int v) => _cache = v; }
                        public sealed class Derived : Base { public string? Name { get; init; } }
                        public record Pos(int A, string B) { public int Computed => A * 2; }
                        public sealed class Prim(int captured, int uncaptured)
                        {
                            public int Twice => captured * 2;
                            public int Stored { get => field; set => field = value; }
                        }

                        [GenerateDeepEquals(typeof(Derived))]
                        [GenerateDeepEquals(typeof(Pos))]
                        [GenerateDeepEquals(typeof(Prim))]
                        [DeepEqualsIgnore(typeof(Base), "_cache")]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        var derived = run.Comparer("Ctx", "Derived");
        var d1 = run.New("Derived"); var d2 = run.New("Derived");
        d1.GetType().GetMethod("Touch")!.Invoke(d1, [1]);
        d2.GetType().GetMethod("Touch")!.Invoke(d2, [2]);
        run.Equals(derived, d1, d2).Should().BeTrue("the cache field is ignored at the context level");
        Set(d2, "Visible", 5);
        run.Equals(derived, d1, d2).Should().BeFalse();

        var pos = run.Comparer("Ctx", "Pos");
        run.Equals(pos, run.New("Pos", 1, "x"), run.New("Pos", 1, "x")).Should().BeTrue();
        run.Equals(pos, run.New("Pos", 1, "x"), run.New("Pos", 2, "x")).Should().BeFalse();

        var prim = run.Comparer("Ctx", "Prim");
        run.Equals(prim, run.New("Prim", 1, 9), run.New("Prim", 1, 8)).Should().BeTrue("the uncaptured parameter has no storage");
        run.Equals(prim, run.New("Prim", 1, 9), run.New("Prim", 2, 9)).Should().BeFalse("the captured parameter is compared");
        var p1 = run.New("Prim", 1, 1); var p2 = run.New("Prim", 1, 1);
        Set(p2, "Stored", 7);
        run.Equals(prim, p1, p2).Should().BeFalse("field-keyword storage is compared");
    }

    [Fact]
    public void Nested_internal_and_generic_types_and_interface_members()
    {
        var run = Clean("""
                        public interface IBusinessObject { int Id { get; } }
                        internal sealed class Outer
                        {
                            internal sealed class Inner : IBusinessObject { public int Id { get; set; } public Wrapper<int>? W; }
                        }
                        public sealed class Wrapper<T> { public T? Value; private T? _hidden; public void Hide(T v) => _hidden = v; }
                        internal sealed class Root { public IBusinessObject? Bo; public Outer.Inner? Inner; }

                        [GenerateDeepEquals(typeof(Root))]
                        internal partial class Ctx : DeepEqualsContextBase { }
                        """);

        var comparer = run.Comparer("Ctx", "Root");
        var a = run.New("Root"); var b = run.New("Root");
        var i1 = run.New("Inner"); var i2 = run.New("Inner");
        Set(i1, "Id", 1); Set(i2, "Id", 1);
        var w1 = Activator.CreateInstance(run.Assembly!.GetTypes().Single(t => t.Name == "Wrapper`1").MakeGenericType(typeof(int)))!;
        var w2 = Activator.CreateInstance(w1.GetType())!;
        Set(w1, "Value", 3); Set(w2, "Value", 3);
        w1.GetType().GetMethod("Hide")!.Invoke(w1, [4]);
        w2.GetType().GetMethod("Hide")!.Invoke(w2, [5]);
        Set(i1, "W", w1); Set(i2, "W", w2);
        Set(a, "Bo", i1); Set(b, "Bo", i2); Set(a, "Inner", i1); Set(b, "Inner", i2);
        run.Equals(comparer, a, b).Should().BeFalse("the private field of the generic type differs");
        w2.GetType().GetMethod("Hide")!.Invoke(w2, [4]);
        run.Equals(comparer, a, b).Should().BeTrue();
        run.Hash(comparer, a).Should().Be(run.Hash(comparer, b));
    }

    [Fact]
    public void Large_predicate_chains_and_small_switch_threshold_produce_the_same_results()
    {
        var members = string.Join("\n", Enumerable.Range(0, 70).Select(i => $"public int F{i};"));
        var source = $$"""
                       public sealed class Wide { {{members}} }
                       public abstract class Base { }
                       {{string.Join("\n", Enumerable.Range(0, 5).Select(i => $"public sealed class D{i} : Base {{ public int V; }}"))}}

                       [GenerateDeepEquals(typeof(Wide))]
                       {{string.Join("\n", Enumerable.Range(0, 5).Select(i => $"[GenerateDeepEquals(typeof(D{i}))]"))}}
                       [DeepEqualsSourceGenerationOptions(MaxSwitchCases = 2, MaxBinaryExpressionArity = 16)]
                       public partial class Ctx : DeepEqualsContextBase { }
                       """;
        var run = Clean(source);
        run.GeneratedSource.Should().Contain("Base_Dispatch", "five exact cases exceed MaxSwitchCases = 2");
        var wide = run.Comparer("Ctx", "Wide");
        var a = run.New("Wide"); var b = run.New("Wide");
        run.Equals(wide, a, b).Should().BeTrue();
        Set(b, "F69", 1);
        run.Equals(wide, a, b).Should().BeFalse();
        var bases = run.Comparer("Ctx", "Base");
        var d3a = run.New("D3"); var d3b = run.New("D3");
        run.Equals(bases, d3a, d3b).Should().BeTrue();
        run.Equals(bases, d3a, run.New("D4")).Should().BeFalse();
    }

    [Fact]
    public void Boxed_struct_cycle_through_object_terminates()
    {
        var run = Clean("""
                        public struct Cell { public int V; public object? Link; }
                        public sealed class Grid { public object? Start; }

                        [GenerateDeepEquals(typeof(Grid))]
                        [GenerateDeepEquals(typeof(Cell))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        run.GeneratedSource.Should().Contain("EqualsBoxed_Cell");
        var comparer = run.Comparer("Ctx", "Grid");
        var cellType = run.Assembly!.GetTypes().Single(t => t.Name == "Cell");
        object MakeSelfCell()
        {
            var boxed = Activator.CreateInstance(cellType)!;
            cellType.GetField("V")!.SetValue(boxed, 1);
            cellType.GetField("Link")!.SetValue(boxed, boxed);   // the box refers to itself
            return boxed;
        }

        var g1 = run.New("Grid"); Set(g1, "Start", MakeSelfCell());
        var g2 = run.New("Grid"); Set(g2, "Start", MakeSelfCell());
        run.Equals(comparer, g1, g2).Should().BeTrue();
        run.Hash(comparer, g1).Should().Be(run.Hash(comparer, g2));
    }

    [Fact]
    public void Hazardous_and_zero_member_types_and_generic_lookup()
    {
        var run = Clean("""
                        public sealed class Instance { public int X; }
                        public sealed class Marker { }
                        public sealed class Root { public Instance? I; public Marker? M; public int? Maybe; }

                        [GenerateDeepEquals(typeof(Root))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """, "DEQ021");

        run.Context("Ctx").GetProperty("Instance").Should().BeNull("the hazardous convenience property is omitted");
        var markers = run.Comparer("Ctx", "Marker");
        run.Equals(markers, run.New("Marker"), run.New("Marker")).Should().BeTrue();
        run.Hash(markers, run.New("Marker")).Should().Be(1);
        var lookup = run.Context("Ctx").GetMethod("GetEqualityComparer")!.MakeGenericMethod(run.Assembly!.GetTypes().Single(t => t.Name == "Instance")).Invoke(null, null)!;
        lookup.Should().NotBeNull();
        var nullableLookup = run.Context("Ctx").GetMethod("GetEqualityComparer")!.MakeGenericMethod(typeof(int?)).Invoke(null, null)!;
        run.Equals(nullableLookup, 1, 1).Should().BeTrue();
        run.Equals(nullableLookup, null, 1).Should().BeFalse();
    }

    [Theory]
    [InlineData("public sealed class A { public int X; } [GenerateDeepEquals(typeof(A))] public partial class Ctx : DeepEqualsContextBase { public int Equals_A; }", "DEQ016")]
    [InlineData("public sealed class A { private class P { } private P? _p; } [GenerateDeepEquals(typeof(A))] public partial class Ctx : DeepEqualsContextBase { }", "DEQ003")]
    [InlineData("public sealed class A { public Action? Cb; } [GenerateDeepEquals(typeof(A))] public partial class Ctx : DeepEqualsContextBase { }", "DEQ005")]
    [InlineData("[GenerateDeepEquals(typeof(List<>))] public partial class Ctx : DeepEqualsContextBase { }", "DEQ010")]
    [InlineData("public unsafe struct A { public int* P; } [GenerateDeepEquals(typeof(A))] public partial class Ctx : DeepEqualsContextBase { }", "DEQ012")]
    [InlineData("public sealed class A { public int[,]? M; } [GenerateDeepEquals(typeof(A))] public partial class Ctx : DeepEqualsContextBase { }", "DEQ014")]
    [InlineData("public interface IId<TSelf> { static abstract TSelf Parse(string s); } public struct K : IId<K> { public static K Parse(string s) => default; } public sealed class A { public IId<K>? Id; } [GenerateDeepEquals(typeof(A))] public partial class Ctx : DeepEqualsContextBase { }", "DEQ012")]
    [InlineData("public interface IId<TSelf> { static abstract TSelf Parse(string s); } public struct K : IId<K> { public static K Parse(string s) => default; } public sealed class A { public List<IId<K>>? Ids; } [GenerateDeepEquals(typeof(A))] public partial class Ctx : DeepEqualsContextBase { }", "DEQ012")]
    [InlineData("public interface IId<TSelf> { static abstract TSelf Parse(string s); } public struct K : IId<K> { public static K Parse(string s) => default; } [GenerateDeepEquals(typeof(IId<K>))] public partial class Ctx : DeepEqualsContextBase { }", "DEQ010")]
    [InlineData("public interface IId<TSelf> { static abstract TSelf Parse(string s); } public struct K : IId<K> { public static K Parse(string s) => default; } [GenerateDeepEquals(typeof(K))] [SimpleType(typeof(IId<K>))] public partial class Ctx : DeepEqualsContextBase { }", "DEQ010")]
    [InlineData("public sealed class A { public int X; } [GenerateDeepEquals(typeof(A))] [DeepEqualsSourceGenerationOptions(MaxSwitchCases = 0)] public partial class Ctx : DeepEqualsContextBase { }", "DEQ013")]
    [InlineData("public sealed class A { public int X; } [GenerateDeepEquals(typeof(A))] [DeepEqualsSourceGenerationOptions(MaxDepth = 0)] public partial class Ctx : DeepEqualsContextBase { }", "DEQ013")]
    [InlineData("public sealed class A { public int X; } [GenerateDeepEquals(typeof(A))] [DeepEqualsSourceGenerationOptions(MatchingHashDepth = 17)] public partial class Ctx : DeepEqualsContextBase { }", "DEQ013")]
    [InlineData("public sealed class A { public int X; } [GenerateDeepEquals(typeof(A))] [DeepEqualsSourceGenerationOptions(CycleHandling = (DeepEqualsCycleHandling)7)] public partial class Ctx : DeepEqualsContextBase { }", "DEQ013")]
    [InlineData("public sealed class A { public int X; } [GenerateDeepEquals(typeof(A))] [DeepEqualsSourceGenerationOptions(CycleHandling = DeepEqualsCycleHandling.Tree, MatchingHashDepth = 2)] public partial class Ctx : DeepEqualsContextBase { }", "DEQ037")]
    [InlineData("public sealed class A { public int X; } [GenerateDeepEquals(typeof(A))] [DeepEqualsSourceGenerationOptions(MaxDepth = 64)] public partial class Ctx : DeepEqualsContextBase { }", "DEQ037")]
    [InlineData("public sealed class A { public int X; } [GenerateDeepEquals(typeof(A))] [DeepEqualsSourceGenerationOptions(CycleHandling = DeepEqualsCycleHandling.Path, MaxDepth = 64)] public partial class Ctx : DeepEqualsContextBase { }", "DEQ037")]
    [InlineData("public sealed class A { public dynamic? D; } [GenerateDeepEquals(typeof(A))] public partial class Ctx : DeepEqualsContextBase { }", "DEQ023")]
    [InlineData("public sealed class A { public System.Text.RegularExpressions.Regex? R; } [GenerateDeepEquals(typeof(A))] public partial class Ctx : DeepEqualsContextBase { }", "DEQ022")]
    [InlineData("public sealed class A { public int X; } [GenerateDeepEquals(typeof(A))] [DeepEqualsIgnore(typeof(A), \"Nope\")] public partial class Ctx : DeepEqualsContextBase { }", "DEQ033")]
    [InlineData("public sealed class A { [DeepEqualsIgnore] public int Computed => 1; public int X; } [GenerateDeepEquals(typeof(A))] public partial class Ctx : DeepEqualsContextBase { }", "DEQ034")]
    [InlineData("public sealed class C : IEqualityComparer<object> { public new bool Equals(object? a, object? b) => true; public int GetHashCode(object o) => 0; } public sealed class A { public int X; } [GenerateDeepEquals(typeof(A))] [CustomEqualityComparer(typeof(C))] public partial class Ctx : DeepEqualsContextBase { }", "DEQ035")]
    [InlineData("public interface IEmpty { } public sealed class A { public IEmpty? E; } [GenerateDeepEquals(typeof(A))] public partial class Ctx : DeepEqualsContextBase { }", "DEQ020")]
    [InlineData("public sealed class Two : IEqualityComparer<int>, IEqualityComparer<long> { public bool Equals(int a, int b) => true; public int GetHashCode(int o) => 0; public bool Equals(long a, long b) => true; public int GetHashCode(long o) => 0; } public sealed class A { public int X; } [GenerateDeepEquals(typeof(A))] [CustomEqualityComparer(typeof(Two))] public partial class Ctx : DeepEqualsContextBase { }", "DEQ006")]
    [InlineData("public abstract class Abs : IEqualityComparer<int> { public bool Equals(int a, int b) => true; public int GetHashCode(int o) => 0; } public sealed class A { public int X; } [GenerateDeepEquals(typeof(A))] [CustomEqualityComparer(typeof(Abs))] public partial class Ctx : DeepEqualsContextBase { }", "DEQ007")]
    [InlineData("public sealed class Bag : IReadOnlyList<int> { private int _extra; public int this[int i] => 0; public int Count => 0; public IEnumerator<int> GetEnumerator() => throw new NotImplementedException(); System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator(); } public sealed class A { public Bag? B; } [GenerateDeepEquals(typeof(A))] public partial class Ctx : DeepEqualsContextBase { }", "DEQ027")]
    [InlineData("public struct Fixed { public unsafe fixed int Buf[4]; } public sealed class A { public Fixed F; } [GenerateDeepEquals(typeof(A))] public partial class Ctx : DeepEqualsContextBase { }", "DEQ032")]
    public void Diagnostics_are_reported(string source, string expected)
    {
        var run = GeneratorHost.Run(Prelude + source, load: false);
        run.GeneratorDiagnosticIds.Should().Contain(expected, run.GeneratedSource);
    }

    [Theory]
    [InlineData("CycleHandling = DeepEqualsCycleHandling.Tree, MaxDepth = 64")]
    [InlineData("CycleHandling = DeepEqualsCycleHandling.Graph, MatchingHashDepth = 2")]
    [InlineData("CycleHandling = DeepEqualsCycleHandling.Path, MatchingHashDepth = 2")]
    [InlineData("CycleHandling = DeepEqualsCycleHandling.Graph")]
    public void An_option_that_applies_to_the_mode_does_not_warn(string options)
    {
        var run = GeneratorHost.Run(Prelude + $"public sealed class A {{ public int X; }} [GenerateDeepEquals(typeof(A))] [DeepEqualsSourceGenerationOptions({options})] public partial class Ctx : DeepEqualsContextBase {{ }}", load: false);
        run.GeneratorDiagnosticIds.Should().NotContain("DEQ037", run.GeneratedSource);
    }

    [Fact]
    public void MaxDepth_outside_Tree_is_ignored_and_the_warning_names_the_mode()
    {
        // Set on a base context and inherited: the mode decided on the derived one still makes the depth inert.
        var run = GeneratorHost.Run(Prelude + """
            public sealed class Node { public int V; public Node? Next; public Node? Other; }
            [DeepEqualsSourceGenerationOptions(MaxDepth = 64)]
            public abstract class BaseCtx : DeepEqualsContextBase { }
            [GenerateDeepEquals(typeof(Node))]
            [DeepEqualsSourceGenerationOptions(CycleHandling = DeepEqualsCycleHandling.Path)]
            public partial class Ctx : BaseCtx { }
            """, load: false);

        var warning = run.GeneratorDiagnostics.Should().ContainSingle(d => d.Id == "DEQ037").Subject;
        warning.Severity.Should().Be(DiagnosticSeverity.Warning);
        warning.GetMessage().Should().Contain("MaxDepth").And.Contain("Path");
        run.GeneratedSource.Should().NotContain("const int MaxDepth", "only Tree emits the depth bound");
    }
}
