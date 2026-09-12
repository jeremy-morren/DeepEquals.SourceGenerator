// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DeepEquals.SourceGeneration.Framework;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DeepEquals.SourceGenerator.Tests;

/// <summary>
/// The fingerprint that buckets set and dictionary entries for matching follows payload edges MatchingHashDepth deep
/// into a recursive type, where the public hash follows one. Equal values must still fingerprint alike, rolled or
/// unrolled, or matching would call them unequal.
/// </summary>
public sealed class MatchingHashDepthTests
{
    private const string Prelude =
        """
        using System;
        using System.Collections.Generic;
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

    private static string Chains(string options) => $$"""
        public sealed class Node { public int Value; public Node? Next; }
        [GenerateDeepEquals(typeof(HashSet<Node>))]
        [DeepEqualsSourceGenerationOptions({{options}})]
        public partial class Ctx : DeepEqualsContextBase { }
        """;

    private static object Node(GeneratorRun run, int value, object? next = null)
    {
        var node = run.New("Node");
        node.GetType().GetField("Value")!.SetValue(node, value);
        node.GetType().GetField("Next")!.SetValue(node, next);
        return node;
    }

    /// <summary>The audit's case: 65 chains 0 -> 0 -> i -> null, which the public hash cannot tell apart.</summary>
    private static object ChainSet(GeneratorRun run)
    {
        var nodeType = run.Assembly!.GetTypes().Single(t => t.Name == "Node");
        var set = (IEnumerable)Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(nodeType))!;
        var add = set.GetType().GetMethod("Add")!;
        for (var i = 0; i <= 64; i++)
            add.Invoke(set, [Node(run, 0, Node(run, 0, Node(run, i)))]);

        return set;
    }

    private static bool SetEquals(GeneratorRun run, object x, object y)
    {
        var comparer = run.Comparer("Ctx", "HashSetOfNode");
        try
        {
            return run.Equals(comparer, x, y);
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }

    [Theory]
    [InlineData("MatchingHashDepth = 1", false)]
    [InlineData("MatchingHashDepth = 1, MaxUnorderedCollisionRun = 65", true)]
    [InlineData("MatchingHashDepth = 2", true)]
    [InlineData("MaxUnorderedCollisionRun = 64", true)]
    [InlineData("MatchingHashDepth = 16", true)]
    public void Matching_hash_depth_separates_chains_that_the_public_hash_does_not(string options, bool succeeds)
    {
        var run = Clean(Chains(options));
        var a = ChainSet(run);
        var b = ChainSet(run);

        var act = () => SetEquals(run, a, b);
        if (succeeds)
            act().Should().BeTrue("the sets hold corresponding equal chains");
        else
            act.Should().Throw<DeepEqualsComplexityException>("at depth 1 all 65 chains share one fingerprint, above the cap of 64");

        // The public hash is unchanged by the option: it still sees one payload edge, so every chain hashes alike.
        var nodes = run.Comparer("Ctx", "Node");
        var hashes = ((IEnumerable)a).Cast<object>().Select(n => run.Hash(nodes, n)).Distinct().ToList();
        hashes.Should().HaveCount(1, "the public hash looks one payload edge into the chain, where every chain is 0 -> 0");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Matching_hash_depth_keeps_coinductive_congruence(int depth)
    {
        var run = Clean(Chains($"MatchingHashDepth = {depth}"));
        var nodeType = run.Assembly!.GetTypes().Single(t => t.Name == "Node");

        object Set(params object[] items)
        {
            var set = Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(nodeType))!;
            foreach (var item in items)
                set.GetType().GetMethod("Add")!.Invoke(set, [item]);

            return set;
        }

        // A one-node cycle, and the same cycle unrolled into two and three nodes.
        var rolled = Node(run, 1);
        rolled.GetType().GetField("Next")!.SetValue(rolled, rolled);
        var twoA = Node(run, 1);
        var twoB = Node(run, 1, twoA);
        twoA.GetType().GetField("Next")!.SetValue(twoA, twoB);
        var threeA = Node(run, 1);
        var threeC = Node(run, 1, threeA);
        var threeB = Node(run, 1, threeC);
        threeA.GetType().GetField("Next")!.SetValue(threeA, threeB);

        SetEquals(run, Set(rolled), Set(twoA)).Should().BeTrue($"a rolled cycle and its unrolling fingerprint alike at depth {depth}");
        SetEquals(run, Set(rolled), Set(threeA)).Should().BeTrue();
        SetEquals(run, Set(twoA, Node(run, 5)), Set(Node(run, 5), threeA)).Should().BeTrue("with other entries beside them, in any order");
        SetEquals(run, Set(rolled), Set(Node(run, 1, Node(run, 2)))).Should().BeFalse("a different value is still unequal");

        var sets = run.Comparer("Ctx", "HashSetOfNode");
        run.Hash(sets, Set(rolled)).Should().Be(run.Hash(sets, Set(twoA)), "the public hash of equal sets is equal");
    }

    [Fact]
    public void Deeper_levels_are_emitted_only_where_a_fingerprint_reaches()
    {
        var run = Clean("""
                        public sealed class Node { public int Value; public Node? Next; }
                        public sealed class Other { public int Value; public Other? Next; }
                        public sealed class Holder { public HashSet<Node>? Nodes; public Other? Chain; }
                        [GenerateDeepEquals(typeof(Holder))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        var source = run.GeneratedSource;
        source.Should().Contain("MatchHashCode_Node_L4(", "the default depth is 4 and Node is a set element");
        source.Should().Contain("MatchHashCode_Node_L2(").And.Contain("MatchHashCode_Node_L3(");
        source.Should().NotContain("MatchHashCode_Node_L5(");
        source.Should().NotContain("MatchHashCode_Other_", "no set or dictionary holds an Other");

        var shallow = Clean("""
                            public sealed class Node { public int Value; public Node? Next; }
                            [GenerateDeepEquals(typeof(HashSet<Node>))]
                            [DeepEqualsSourceGenerationOptions(MatchingHashDepth = 1)]
                            public partial class Ctx : DeepEqualsContextBase { }
                            """);
        shallow.GeneratedSource.Should().NotContain("MatchHashCode_", "depth 1 is the public hash");
    }

    [Fact]
    public void Deeper_levels_run_through_lists_dictionaries_tuples_and_dispatch()
    {
        var run = Clean($$"""
                          public abstract class Shape { public int Id; }
                          public sealed class Group : Shape { public List<Shape>? Items; public Dictionary<string, Shape>? Named; public (int, Shape?) Pair; }
                          public sealed class Leaf : Shape { public double Size; }
                          [GenerateDeepEquals(typeof(HashSet<Shape>))]
                          [GenerateDeepEquals(typeof(Group))]
                          [GenerateDeepEquals(typeof(Leaf))]
                          public partial class Ctx : DeepEqualsContextBase { }
                          """);

        var source = run.GeneratedSource;
        source.Should().Contain("MatchHashCode_Shape_L4(", "the dispatch core").And.Contain("MatchHashCode_Group_L4(", "a sealed case")
            .And.Contain("MatchHashCode_ListOfShape_L4(", "a list in the cycle").And.Contain("MatchHashCode_DictionaryOfStringAndShape_L4(", "a dictionary in the cycle");

        object Leaf(int id, double size)
        {
            var leaf = run.New("Leaf");
            leaf.GetType().GetField("Id")!.SetValue(leaf, id);
            leaf.GetType().GetField("Size")!.SetValue(leaf, size);
            return leaf;
        }

        var shapeType = run.Assembly!.GetTypes().Single(t => t.Name == "Shape");
        object Group(int id, params object[] items)
        {
            var group = run.New("Group");
            group.GetType().GetField("Id")!.SetValue(group, id);
            var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(shapeType))!;
            foreach (var item in items) list.Add(item);
            group.GetType().GetField("Items")!.SetValue(group, list);
            return group;
        }

        // Sixty groups whose only difference is two levels down: the public hash cannot tell them apart, the fingerprint can.
        object Set(IEnumerable<object> items)
        {
            var set = Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(shapeType))!;
            foreach (var item in items) set.GetType().GetMethod("Add")!.Invoke(set, [item]);
            return set;
        }

        var left = Set(Enumerable.Range(0, 70).Select(i => Group(0, Group(0, Leaf(0, i)))));
        var right = Set(Enumerable.Range(0, 70).Reverse().Select(i => Group(0, Group(0, Leaf(0, i)))));
        var sets = run.Comparer("Ctx", "HashSetOfShape");
        run.Equals(sets, left, right).Should().BeTrue("70 entries sharing a public hash, separated by the fingerprint");
        run.Hash(sets, left).Should().Be(run.Hash(sets, right));
    }
}
