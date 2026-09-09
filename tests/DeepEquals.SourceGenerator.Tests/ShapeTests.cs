// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;
using Xunit.Abstractions;

namespace DeepEquals.SourceGenerator.Tests;

public sealed class ShapeTests
{
    private readonly ITestOutputHelper _output;

    public ShapeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private const string Prelude = """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using DeepEquals.SourceGeneration;
        namespace Tests;
        """;

    private GeneratorRun Clean(string source, params string[] expectedWarnings)
    {
        var run = GeneratorHost.Run(Prelude + source);
        var dump = Environment.GetEnvironmentVariable("DEEPEQUALS_DUMP");
        if (dump is not null)
        {
            System.IO.Directory.CreateDirectory(dump);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dump, "shape.g.cs"), run.GeneratedSource);
        }

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
    public void Upward_crawl_skips_interfaces_with_unimplemented_static_abstract_members()
    {
        var run = Clean("""
                        public interface IId<TSelf, TRaw> : IEquatable<TSelf> where TSelf : struct, IId<TSelf, TRaw>
                        {
                            TRaw Serialize();
                            static abstract TSelf Deserialize(TRaw raw);
                        }

                        public interface IWithDefault<TSelf>
                        {
                            static virtual TSelf Create() => default!;
                        }

                        public readonly struct SkuId : IId<SkuId, string>, IWithDefault<SkuId>
                        {
                            public readonly string Value;
                            public SkuId(string value) { Value = value; }
                            public string Serialize() => Value;
                            public static SkuId Deserialize(string raw) => new SkuId(raw);
                            public bool Equals(SkuId other) => Value == other.Value;
                        }

                        public sealed class Product { public SkuId Id; }

                        [GenerateDeepEquals(typeof(Product))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        run.GeneratedSource.Should().NotContain("IIdOf", "an interface with an unimplemented static abstract member cannot be an IEqualityComparer<T> argument");
        run.GeneratedSource.Should().Contain("IWithDefaultOfSkuId", "a static virtual member with a body does not disqualify the interface");
        var products = run.Comparer("Ctx", "Product");
        var a = run.New("Product"); var b = run.New("Product");
        var sku = run.Assembly!.GetTypes().Single(t => t.Name == "SkuId");
        Set(a, "Id", Activator.CreateInstance(sku, "x")); Set(b, "Id", Activator.CreateInstance(sku, "x"));
        run.Equals(products, a, b).Should().BeTrue();
        Set(b, "Id", Activator.CreateInstance(sku, "y"));
        run.Equals(products, a, b).Should().BeFalse();
    }

    [Fact]
    public void Containers_behind_object_select_canonical_family_cases()
    {
        var run = Clean("""
                        public sealed class Bag
                        {
                            public object? Any;
                            public int[]? Ints;
                            public HashSet<string>? Set;
                            public Dictionary<string, int>? Map;
                        }

                        [GenerateDeepEquals(typeof(Bag))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        var objects = run.Comparer("Ctx", "Object");
        run.Equals(objects, new[] { 1, 2, 3 }, new List<int> { 1, 2, 3 }).Should().BeTrue("an array and a list select the same ordered case");
        run.Hash(objects, new[] { 1, 2, 3 }).Should().Be(run.Hash(objects, new List<int> { 1, 2, 3 }));
        run.Equals(objects, new HashSet<string> { "a", "b" }, new SortedSet<string> { "b", "a" }).Should().BeTrue("both select the set case");
        run.Equals(objects, new HashSet<string> { "a", "b" }, new List<string> { "a", "b" }).Should().BeFalse("a set and an ordered-only implementation never fall through to each other");
        run.Equals(objects, new Dictionary<string, int> { ["a"] = 1 }, new SortedDictionary<string, int> { ["a"] = 1 }).Should().BeTrue("dictionary vs sorted dictionary");
        run.Equals(objects, new[] { 1, 2 }, new[] { 2, 1 }).Should().BeFalse("order matters");

        var bags = run.Comparer("Ctx", "Bag");
        var a = run.New("Bag"); var b = run.New("Bag");
        Set(a, "Any", new[] { 7 }); Set(b, "Any", new List<int> { 7 });
        run.Equals(bags, a, b).Should().BeTrue("int array vs list behind an object member share the reached ordered-int case");
        run.Hash(bags, a).Should().Be(run.Hash(bags, b));
        Set(a, "Any", new[] { "x" }); Set(b, "Any", new List<string> { "x" });
        run.Equals(bags, a, b).Should().BeFalse("no ordered container of string was reached, so the two runtime types are simply different");
    }

    [Fact]
    public void Memory_immutable_array_and_array_segment_compare_their_represented_sequences()
    {
        var run = Clean("""
                        public sealed class Slices
                        {
                            public ReadOnlyMemory<byte> Bytes;
                            public Memory<int> Ints;
                            public ImmutableArray<string> Names;
                            public ArraySegment<int> Segment;
                            public object? Boxed;
                        }

                        [GenerateDeepEquals(typeof(Slices))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        var comparer = run.Comparer("Ctx", "Slices");
        var a = run.New("Slices"); var b = run.New("Slices");
        byte[] backingA = [9, 1, 2, 3, 9];
        byte[] backingB = [1, 2, 3];
        Set(a, "Bytes", new ReadOnlyMemory<byte>(backingA, 1, 3));
        Set(b, "Bytes", new ReadOnlyMemory<byte>(backingB));
        Set(a, "Ints", new Memory<int>([4, 5]));
        Set(b, "Ints", new Memory<int>([0, 4, 5, 0], 1, 2));
        Set(a, "Names", ImmutableArray.Create("p", "q"));
        Set(b, "Names", ImmutableArray.Create("p", "q"));
        Set(a, "Segment", new ArraySegment<int>([7, 8, 9], 1, 2));
        Set(b, "Segment", new ArraySegment<int>([8, 9]));
        run.Equals(comparer, a, b).Should().BeTrue("slices compare by contents, not by owner or offset");
        run.Hash(comparer, a).Should().Be(run.Hash(comparer, b));

        Set(b, "Names", default(ImmutableArray<string>));
        run.Equals(comparer, a, b).Should().BeFalse("a default immutable array differs from an initialized one");
        Set(a, "Names", default(ImmutableArray<string>));
        run.Equals(comparer, a, b).Should().BeTrue("two defaults are equal");
        run.Hash(comparer, a).Should().Be(run.Hash(comparer, b));

        Set(a, "Segment", default(ArraySegment<int>));
        Set(b, "Segment", new ArraySegment<int>([]));
        run.Equals(comparer, a, b).Should().BeTrue("a default segment equals an initialized empty one");
        run.Hash(comparer, a).Should().Be(run.Hash(comparer, b));

        Set(a, "Boxed", ImmutableArray.Create(1, 2));
        Set(b, "Boxed", new[] { 1, 2 });
        run.Equals(comparer, a, b).Should().BeTrue("a boxed immutable array behind object takes the canonical ordered case");
    }

    [Fact]
    public void Nullable_only_custom_registration_serves_the_non_nullable_value_by_wrapping()
    {
        var run = Clean("""
                        public struct Money { public decimal Amount; public string? Currency; }
                        public sealed class MoneyComparer : IEqualityComparer<Money?>
                        {
                            public bool Equals(Money? x, Money? y) => x!.Value.Amount == y!.Value.Amount;
                            public int GetHashCode(Money? o) => o!.Value.Amount.GetHashCode();
                        }
                        public sealed class Order { public Money Total; public Money? Discount; }

                        [GenerateDeepEquals(typeof(Order))]
                        [CustomEqualityComparer(typeof(MoneyComparer))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """, "DEQ029");

        var comparer = run.Comparer("Ctx", "Order");
        var money = run.Assembly!.GetTypes().Single(t => t.Name == "Money");
        var m1 = Activator.CreateInstance(money)!; money.GetField("Amount")!.SetValue(m1, 1.0m); money.GetField("Currency")!.SetValue(m1, "USD");
        var m2 = Activator.CreateInstance(money)!; money.GetField("Amount")!.SetValue(m2, 1.00m); money.GetField("Currency")!.SetValue(m2, "EUR");
        var a = run.New("Order"); var b = run.New("Order");
        Set(a, "Total", m1); Set(b, "Total", m2);
        run.Equals(comparer, a, b).Should().BeTrue("the non-nullable member is wrapped and handed to the nullable comparer");
        run.Hash(comparer, a).Should().Be(run.Hash(comparer, b));
        Set(a, "Discount", m1);
        run.Equals(comparer, a, b).Should().BeFalse("an absent nullable differs from a present one");
        Set(b, "Discount", m2);
        run.Equals(comparer, a, b).Should().BeTrue();
    }

    [Theory]
    [InlineData("public class Base { } public sealed class Derived : Base { } public sealed class A { public Derived? D; } [GenerateDeepEquals(typeof(A))] [SimpleType(typeof(Base))] [SimpleType(typeof(Derived))] public partial class Ctx : DeepEqualsContextBase { }", "DEQ025")]
    [InlineData("public sealed class S { } public sealed class C : IEqualityComparer<S> { public bool Equals(S? a, S? b) => true; public int GetHashCode(S o) => 0; } public sealed class A { public S? V; } [GenerateDeepEquals(typeof(A))] [SimpleType(typeof(S))] [CustomEqualityComparer(typeof(C))] public partial class Ctx : DeepEqualsContextBase { }", "DEQ008")]
    [InlineData("public sealed class P([DeepEqualsIgnore] int unused) { public int X; } [GenerateDeepEquals(typeof(P))] public partial class Ctx : DeepEqualsContextBase { }", "DEQ034")]
    public void Overlap_and_no_op_diagnostics(string source, string expected)
    {
        var run = GeneratorHost.Run(Prelude + source, load: false);
        run.GeneratorDiagnosticIds.Should().Contain(expected);
    }
}
