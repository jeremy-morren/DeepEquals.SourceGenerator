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
        GeneratorRun run = GeneratorHost.Run(Prelude + source);
        if (run.CompileErrors.Any() || run.GeneratorDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            _output.WriteLine(run.GeneratedSource);
            _output.WriteLine(string.Join(Environment.NewLine, run.CompileErrors.Concat(run.GeneratorDiagnostics.Select(d => d.ToString()))));
        }

        run.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        run.CompileErrors.Should().BeEmpty();
        run.Assembly.Should().NotBeNull();
        foreach (string id in expectedWarnings)
        {
            run.GeneratorDiagnosticIds.Should().Contain(id);
        }

        return run;
    }

    private static GeneratorRun Diagnose(string source) => GeneratorHost.Run(Prelude + source, load: false);

    private static void Set(object target, string member, object? value)
    {
        Type type = target.GetType();
        System.Reflection.FieldInfo? field = type.GetField(member, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
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
        GeneratorRun run = Clean("""
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

        object comparer = run.Comparer("Ctx", "Order");
        object a = run.New("Order"); object b = run.New("Order");
        object m1 = run.New("Money"); Set(m1, "Amount", 1.0m); Set(m1, "Currency", "USD");
        object m2 = run.New("Money"); Set(m2, "Amount", 1.00m); Set(m2, "Currency", "EUR");
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
        GeneratorRun run = Clean("""
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

        object comparer = run.Comparer("Ctx", "Card");
        object a = run.New("Card"); object b = run.New("Card");
        object empty = run.New("Label"); Set(empty, "Text", "");
        Set(a, "Front", null); Set(b, "Front", empty);
        run.Equals(comparer, a, b).Should().BeTrue("the comparer decided that null equals an empty label");
        run.Hash(comparer, a).Should().Be(run.Hash(comparer, b));
    }

    [Fact]
    public void Simple_type_is_a_leaf_compared_with_default_equality()
    {
        GeneratorRun run = Clean("""
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

        object comparer = run.Comparer("Ctx", "Item");
        object a = run.New("Item"); object b = run.New("Item");
        object s1 = run.New("Sku"); Set(s1, "Code", "X"); Set(s1, "Ignored", 1);
        object s2 = run.New("Sku"); Set(s2, "Code", "X"); Set(s2, "Ignored", 2);
        Set(a, "Sku", s1); Set(b, "Sku", s2);
        run.Equals(comparer, a, b).Should().BeTrue("Sku uses its own Equals, which ignores the second field");
        run.Hash(comparer, a).Should().Be(run.Hash(comparer, b));
        run.GeneratedSource.Should().Contain(".Equals(", "a sealed class with a public Equals(Sku) is called directly");
        run.GeneratedSource.Should().NotContain("EqualityComparer<global::Tests.Sku>.Default");
    }

    [Fact]
    public void Simple_types_implementing_IEquatable_of_self_bypass_the_default_comparer()
    {
        GeneratorRun run = Clean("""
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

        string source = run.GeneratedSource;
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

        object comparer = run.Comparer("Ctx", "Holder");
        object a = run.New("Holder"); object b = run.New("Holder");
        Set(a, "A", run.New("Id1", "x")); Set(b, "A", run.New("Id1", "x"));
        Set(a, "B", run.New("Id2", "abc")); Set(b, "B", run.New("Id2", "ABC"));
        Set(a, "C", run.New("Id3", "abc")); Set(b, "C", run.New("Id3", "ABC"));
        Set(a, "D", run.New("Id2", "q")); Set(b, "D", run.New("Id2", "Q"));
        Set(a, "E", run.New("Id1", "e")); Set(b, "E", run.New("Id1", "e"));
        object f1 = run.New("Base"); Set(f1, "Key", "k"); object f2 = run.New("Base"); Set(f2, "Key", "K");
        Set(a, "F", f1); Set(b, "F", f2);
        object g1 = run.New("Derived"); Set(g1, "Key", "d"); object g2 = run.New("Derived"); Set(g2, "Key", "D");
        Set(a, "G", g1); Set(b, "G", g2);
        Type id2 = run.Assembly!.GetTypes().Single(t => t.Name == "Id2");
        System.Collections.IList h1 = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(id2))!;
        System.Collections.IList h2 = (System.Collections.IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(id2))!;
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
        GeneratorRun run = Clean("""
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

        object derived = run.Comparer("Ctx", "Derived");
        object d1 = run.New("Derived"); object d2 = run.New("Derived");
        d1.GetType().GetMethod("Touch")!.Invoke(d1, new object[] { 1 });
        d2.GetType().GetMethod("Touch")!.Invoke(d2, new object[] { 2 });
        run.Equals(derived, d1, d2).Should().BeTrue("the cache field is ignored at the context level");
        Set(d2, "Visible", 5);
        run.Equals(derived, d1, d2).Should().BeFalse();

        object pos = run.Comparer("Ctx", "Pos");
        run.Equals(pos, run.New("Pos", 1, "x"), run.New("Pos", 1, "x")).Should().BeTrue();
        run.Equals(pos, run.New("Pos", 1, "x"), run.New("Pos", 2, "x")).Should().BeFalse();

        object prim = run.Comparer("Ctx", "Prim");
        run.Equals(prim, run.New("Prim", 1, 9), run.New("Prim", 1, 8)).Should().BeTrue("the uncaptured parameter has no storage");
        run.Equals(prim, run.New("Prim", 1, 9), run.New("Prim", 2, 9)).Should().BeFalse("the captured parameter is compared");
        object p1 = run.New("Prim", 1, 1); object p2 = run.New("Prim", 1, 1);
        Set(p2, "Stored", 7);
        run.Equals(prim, p1, p2).Should().BeFalse("field-keyword storage is compared");
    }

    [Fact]
    public void Nested_internal_and_generic_types_and_interface_members()
    {
        GeneratorRun run = Clean("""
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

        object comparer = run.Comparer("Ctx", "Root");
        object a = run.New("Root"); object b = run.New("Root");
        object i1 = run.New("Inner"); object i2 = run.New("Inner");
        Set(i1, "Id", 1); Set(i2, "Id", 1);
        object w1 = Activator.CreateInstance(run.Assembly!.GetTypes().Single(t => t.Name == "Wrapper`1").MakeGenericType(typeof(int)))!;
        object w2 = Activator.CreateInstance(w1.GetType())!;
        Set(w1, "Value", 3); Set(w2, "Value", 3);
        w1.GetType().GetMethod("Hide")!.Invoke(w1, new object[] { 4 });
        w2.GetType().GetMethod("Hide")!.Invoke(w2, new object[] { 5 });
        Set(i1, "W", w1); Set(i2, "W", w2);
        Set(a, "Bo", i1); Set(b, "Bo", i2); Set(a, "Inner", i1); Set(b, "Inner", i2);
        run.Equals(comparer, a, b).Should().BeFalse("the private field of the generic type differs");
        w2.GetType().GetMethod("Hide")!.Invoke(w2, new object[] { 4 });
        run.Equals(comparer, a, b).Should().BeTrue();
        run.Hash(comparer, a).Should().Be(run.Hash(comparer, b));
    }

    [Fact]
    public void Large_predicate_chains_and_small_switch_threshold_produce_the_same_results()
    {
        string members = string.Join("\n", Enumerable.Range(0, 70).Select(i => $"public int F{i};"));
        string source = $$"""
            public sealed class Wide { {{members}} }
            public abstract class Base { }
            {{string.Join("\n", Enumerable.Range(0, 5).Select(i => $"public sealed class D{i} : Base {{ public int V; }}"))}}

            [GenerateDeepEquals(typeof(Wide))]
            {{string.Join("\n", Enumerable.Range(0, 5).Select(i => $"[GenerateDeepEquals(typeof(D{i}))]"))}}
            [DeepEqualsSourceGenerationOptions(MaxSwitchCases = 2, MaxBinaryExpressionArity = 16)]
            public partial class Ctx : DeepEqualsContextBase { }
            """;
        GeneratorRun run = Clean(source);
        run.GeneratedSource.Should().Contain("Base_Dispatch", "five exact cases exceed MaxSwitchCases = 2");
        object wide = run.Comparer("Ctx", "Wide");
        object a = run.New("Wide"); object b = run.New("Wide");
        run.Equals(wide, a, b).Should().BeTrue();
        Set(b, "F69", 1);
        run.Equals(wide, a, b).Should().BeFalse();
        object bases = run.Comparer("Ctx", "Base");
        object d3a = run.New("D3"); object d3b = run.New("D3");
        run.Equals(bases, d3a, d3b).Should().BeTrue();
        run.Equals(bases, d3a, run.New("D4")).Should().BeFalse();
    }

    [Fact]
    public void Boxed_struct_cycle_through_object_terminates()
    {
        GeneratorRun run = Clean("""
            public struct Cell { public int V; public object? Link; }
            public sealed class Grid { public object? Start; }

            [GenerateDeepEquals(typeof(Grid))]
            [GenerateDeepEquals(typeof(Cell))]
            public partial class Ctx : DeepEqualsContextBase { }
            """);

        run.GeneratedSource.Should().Contain("EqualsBoxed_Cell");
        object comparer = run.Comparer("Ctx", "Grid");
        Type cellType = run.Assembly!.GetTypes().Single(t => t.Name == "Cell");
        object MakeSelfCell()
        {
            object boxed = Activator.CreateInstance(cellType)!;
            cellType.GetField("V")!.SetValue(boxed, 1);
            cellType.GetField("Link")!.SetValue(boxed, boxed);   // the box refers to itself
            return boxed;
        }

        object g1 = run.New("Grid"); Set(g1, "Start", MakeSelfCell());
        object g2 = run.New("Grid"); Set(g2, "Start", MakeSelfCell());
        run.Equals(comparer, g1, g2).Should().BeTrue();
        run.Hash(comparer, g1).Should().Be(run.Hash(comparer, g2));
    }

    [Fact]
    public void Hazardous_and_zero_member_types_and_generic_lookup()
    {
        GeneratorRun run = Clean("""
            public sealed class Instance { public int X; }
            public sealed class Marker { }
            public sealed class Root { public Instance? I; public Marker? M; public int? Maybe; }

            [GenerateDeepEquals(typeof(Root))]
            public partial class Ctx : DeepEqualsContextBase { }
            """, "DEQ021");

        run.Context("Ctx").GetProperty("Instance").Should().BeNull("the hazardous convenience property is omitted");
        object markers = run.Comparer("Ctx", "Marker");
        run.Equals(markers, run.New("Marker"), run.New("Marker")).Should().BeTrue();
        run.Hash(markers, run.New("Marker")).Should().Be(1);
        object lookup = run.Context("Ctx").GetMethod("GetEqualityComparer")!.MakeGenericMethod(run.Assembly!.GetTypes().Single(t => t.Name == "Instance")).Invoke(null, null)!;
        lookup.Should().NotBeNull();
        object nullableLookup = run.Context("Ctx").GetMethod("GetEqualityComparer")!.MakeGenericMethod(typeof(int?)).Invoke(null, null)!;
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
        GeneratorRun run = GeneratorHost.Run(Prelude + source, load: false);
        run.GeneratorDiagnosticIds.Should().Contain(expected, run.GeneratedSource);
    }
}
