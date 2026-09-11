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

namespace DeepEquals.SourceGenerator.Tests;

/// <summary>
/// The fast paths must give exactly the answers of the general rules they short-circuit: a dictionary walked through its
/// own lookup, a span taken from an immutable array behind an interface, a runtime type recognized before the assignable
/// chain, and wide leaves hashed word by word.
/// </summary>
public sealed class FastPathTests
{
    private const string Prelude =
        """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
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

    [Fact]
    public void Dictionary_with_default_key_equality_walks_its_own_lookup_and_others_stay_unordered()
    {
        var run = Clean("""
                        public sealed class Bag
                        {
                            public Dictionary<string, int>? Counts;
                            public IReadOnlyDictionary<string, List<int>>? Deep;
                        }

                        [GenerateDeepEquals(typeof(Bag))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        var counts = run.Comparer("Ctx", "DictionaryOfStringAndInt32");
        var a = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 };
        var b = new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 };
        run.Equals(counts, a, b).Should().BeTrue("same entries, default comparers, any order");
        run.Hash(counts, a).Should().Be(run.Hash(counts, b));

        var ordinal = new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1, ["b"] = 2 };
        run.Equals(counts, a, ordinal).Should().BeTrue("the ordinal comparer is the same equality under another instance");

        var differentValue = new Dictionary<string, int> { ["a"] = 1, ["b"] = 3 };
        run.Equals(counts, a, differentValue).Should().BeFalse();
        var differentKey = new Dictionary<string, int> { ["a"] = 1, ["c"] = 2 };
        run.Equals(counts, a, differentKey).Should().BeFalse();

        // A case-insensitive dictionary groups keys the context does not consider equal, so it takes the general path.
        var ignoreCase = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["A"] = 1, ["B"] = 2 };
        run.Equals(counts, a, ignoreCase).Should().BeFalse("'A' and 'a' differ ordinally");
        var ignoreCaseSame = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["a"] = 1, ["b"] = 2 };
        run.Equals(counts, a, ignoreCaseSame).Should().BeTrue("the same ordinal keys, whatever comparer built the dictionary");

        // The interface view takes the same fast path when both runtime types are dictionaries, and the general one otherwise.
        var deep = run.Comparer("Ctx", "IReadOnlyDictionaryOfStringAndListOfInt32");
        var d1 = new Dictionary<string, List<int>> { ["k"] = [1, 2] };
        var d2 = new Dictionary<string, List<int>> { ["k"] = [1, 2] };
        var d3 = new SortedDictionary<string, List<int>> { ["k"] = [1, 2] };
        var d4 = new Dictionary<string, List<int>> { ["k"] = [1, 3] };
        run.Equals(deep, d1, d2).Should().BeTrue();
        run.Equals(deep, d1, d3).Should().BeTrue("a sorted dictionary is compared as an unordered multiset");
        run.Equals(deep, d1, d4).Should().BeFalse("values are deep-compared");
        run.Hash(deep, d1).Should().Be(run.Hash(deep, d3));
    }

    [Fact]
    public void Immutable_arrays_and_lists_behind_interfaces_compare_and_hash_through_spans()
    {
        var run = Clean("""
                        public sealed class Bag
                        {
                            public IReadOnlyList<int>? Items;
                            public IEnumerable<string>? Words;
                        }

                        [GenerateDeepEquals(typeof(Bag))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        var items = run.Comparer("Ctx", "IReadOnlyListOfInt32");
        var immutable = ImmutableArray.Create(1, 2, 3);
        var list = new List<int> { 1, 2, 3 };
        int[] array = [1, 2, 3];
        run.Equals(items, immutable, list).Should().BeTrue();
        run.Equals(items, immutable, array).Should().BeTrue();
        run.Equals(items, list, array).Should().BeTrue();
        run.Equals(items, immutable, new List<int> { 1, 2 }).Should().BeFalse();
        run.Hash(items, immutable).Should().Be(run.Hash(items, list));
        run.Hash(items, list).Should().Be(run.Hash(items, array));

        run.Equals(items, default(ImmutableArray<int>), default(ImmutableArray<int>)).Should().BeTrue("two default immutable arrays are equal");
        run.Equals(items, default(ImmutableArray<int>), ImmutableArray<int>.Empty).Should().BeFalse("default is not empty");
        run.Equals(items, ImmutableArray<int>.Empty, default(ImmutableArray<int>)).Should().BeFalse();
        run.Equals(items, new List<int>(), default(ImmutableArray<int>)).Should().BeFalse();
        run.Hash(items, default(ImmutableArray<int>)).Should().Be(0);

        var words = run.Comparer("Ctx", "IEnumerableOfString");
        run.Equals(words, ImmutableArray.Create("a", "b"), new[] { "a", "b" }).Should().BeTrue();
        run.Equals(words, new[] { "a", "b" }, Enumerate("a", "b")).Should().BeTrue("an iterator falls back to enumeration");
        run.Equals(words, Enumerate("a", "b"), Enumerate("a", "c")).Should().BeFalse();
        run.Hash(words, ImmutableArray.Create("a", "b")).Should().Be(run.Hash(words, Enumerate("a", "b")));
    }

    private static IEnumerable<string> Enumerate(params string[] values)
    {
        foreach (var value in values)
            yield return value;
    }

    [Fact]
    public void Known_runtime_types_behind_object_land_where_the_assignable_chain_would()
    {
        var run = Clean("""
                        public interface IThing { }
                        public sealed class Widget : IThing { public int Size; }
                        public sealed class Gadget { public int Size; }
                        public sealed class Bag { public object? Any; public IThing? Thing; public Widget? W; public Gadget? G; public List<int>? Ints; }

                        public sealed class ThingComparer : IEqualityComparer<IThing>
                        {
                            public bool Equals(IThing? x, IThing? y) => true;
                            public int GetHashCode(IThing o) => 1;
                        }

                        [GenerateDeepEquals(typeof(Bag))]
                        [CustomEqualityComparer(typeof(ThingComparer))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        var objects = run.Comparer("Ctx", "Object");
        var w1 = run.New("Widget"); var w2 = run.New("Widget");
        Set(w1, "Size", 1); Set(w2, "Size", 2);
        run.Equals(objects, w1, w2).Should().BeTrue("Widget is a reached deep type that converts to IThing, whose custom comparer says everything is equal");
        run.Hash(objects, w1).Should().Be(1);

        var g1 = run.New("Gadget"); var g2 = run.New("Gadget");
        Set(g1, "Size", 1); Set(g2, "Size", 1);
        run.Equals(objects, g1, g2).Should().BeTrue("a sealed class with no assignable case compares by members");
        Set(g2, "Size", 2);
        run.Equals(objects, g1, g2).Should().BeFalse();
        run.Equals(objects, g1, w1).Should().BeFalse("different runtime types");
        run.Equals(objects, new List<int> { 1 }, new[] { 1 }).Should().BeTrue("both select the ordered-int case");
        run.Equals(objects, 1, 1).Should().BeTrue();
        run.Equals(objects, 1, 2).Should().BeFalse();
        run.Equals(objects, 1, 1L).Should().BeFalse("int and long are different runtime types");
        run.Equals(objects, new object(), new object()).Should().BeTrue("two plain objects have no state to differ in");

        run.Equals(objects, new Uri("http://a"), new Uri("http://a")).Should().BeTrue("every built-in leaf is a case of the object comparer, reached or not");
        var act = () => run.Equals(objects, new System.Text.StringBuilder("a"), new System.Text.StringBuilder("a"));
        act.Should().Throw<System.Reflection.TargetInvocationException>()
            .WithInnerException<SourceGeneration.Framework.DeepEqualsUnknownTypeException>("StringBuilder is neither a leaf nor reached");
        run.Equals(objects, new System.Text.StringBuilder("a"), "text").Should().BeFalse("different unknown and known runtime types are simply unequal");
    }

    [Theory]
    [InlineData("")]
    [InlineData(Hashing64Tests.Options64)]
    public void Wide_leaves_hash_by_word_and_agree_with_equality(string options)
    {
        var run = Clean($$"""
                        public struct Pair { public long A; public double B; }
                        public sealed class Wide
                        {
                            public long L; public ulong UL; public double D; public decimal M; public Guid G;
                            public DateTime T; public DateTimeOffset O; public Pair P; public decimal? MaybeM; public float F;
                        }

                        {{options}}
                        [GenerateDeepEquals(typeof(Wide))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        var wide = run.Comparer("Ctx", "Wide");
        var a = run.New("Wide"); var b = run.New("Wide");
        foreach (var target in new[] { a, b })
        {
            Set(target, "L", long.MaxValue - 5); Set(target, "UL", ulong.MaxValue - 5); Set(target, "D", 1.5);
            Set(target, "M", 1.50m); Set(target, "G", new Guid("11111111-2222-3333-4444-555555555555"));
            Set(target, "T", new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));
            Set(target, "O", new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.FromHours(3)));
            Set(target, "MaybeM", 2.5m); Set(target, "F", 0.25f);
            var pair = run.New("Pair");
            Set(pair, "A", 7L); Set(pair, "B", -0.0);
            Set(target, "P", pair);
        }

        run.Equals(wide, a, b).Should().BeTrue();
        run.Hash(wide, a).Should().Be(run.Hash(wide, b));

        Set(b, "M", 1.5m);
        run.Equals(wide, a, b).Should().BeFalse("1.50m and 1.5m differ in scale");
        Set(b, "M", 1.50m);

        Set(b, "T", new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Local));
        run.Equals(wide, a, b).Should().BeFalse("the kind is part of the value");
        Set(b, "T", new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc));

        Set(b, "G", new Guid("11111111-2222-3333-4444-555555555556"));
        run.Equals(wide, a, b).Should().BeFalse();
        run.Hash(wide, a).Should().NotBe(run.Hash(wide, b), "a change in the last Guid byte, which Guid.GetHashCode ignores, changes the hash");
    }

    [Theory]
    [InlineData("")]
    [InlineData("[DeepEqualsSourceGenerationOptions(CycleHandling = DeepEqualsCycleHandling.Path)]")]
    public void One_guard_per_cycle_still_terminates_and_equates_rolled_and_unrolled_graphs(string options)
    {
        var run = Clean($$"""
                        public sealed class TreeNode { public int Value; public List<TreeNode>? Children; }

                        {{options}}
                        [GenerateDeepEquals(typeof(TreeNode))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        run.GeneratedSource.Should().Contain("Kind_TreeNode", "the class body keeps the guard its cycle needs");
        run.GeneratedSource.Should().NotContain("Kind_ListOfTreeNode", "the list's cycle already passes through the class guard");

        var nodes = run.Comparer("Ctx", "TreeNode");
        object Node(int value, params object[] children)
        {
            var node = run.New("TreeNode");
            Set(node, "Value", value);
            var list = (System.Collections.IList)System.Activator.CreateInstance(node.GetType().GetField("Children")!.FieldType)!;
            foreach (var child in children) list.Add(child);
            Set(node, "Children", list);
            return node;
        }

        // A node whose child list holds the node itself, against the same cycle unrolled once more.
        var rolled = Node(1);
        ((System.Collections.IList)rolled.GetType().GetField("Children")!.GetValue(rolled)!).Add(rolled);
        var unrolledInner = Node(1);
        var unrolled = Node(1, unrolledInner);
        ((System.Collections.IList)unrolledInner.GetType().GetField("Children")!.GetValue(unrolledInner)!).Add(unrolled);

        run.Equals(nodes, rolled, unrolled).Should().BeTrue("a rolled cycle equals its unrolled form");
        run.Hash(nodes, rolled).Should().Be(run.Hash(nodes, unrolled));

        var different = Node(1, Node(2));
        run.Equals(nodes, rolled, different).Should().BeFalse();

        // A deep tree compares without recursion trouble, and a difference at the bottom is seen.
        var left = Node(0, Node(1, Node(2, Node(3))), Node(4));
        var right = Node(0, Node(1, Node(2, Node(3))), Node(4));
        run.Equals(nodes, left, right).Should().BeTrue();
        var changed = Node(0, Node(1, Node(2, Node(5))), Node(4));
        run.Equals(nodes, left, changed).Should().BeFalse();
    }

    private static void Set(object target, string member, object? value)
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
}
