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
/// CycleHandling: Graph retains every guarded pair, Path only the ancestors of the current one, Tree none, bounding
/// the traversal with a depth instead. Graph and Path answer alike on every input; Tree answers alike on acyclic data.
/// </summary>
public sealed class CycleHandlingTests
{
    private const string Prelude =
        """
        using System;
        using System.Collections.Generic;
        using DeepEquals.SourceGeneration;
        namespace Tests;
        """;

    internal static GeneratorRun Clean(string source)
    {
        var run = GeneratorHost.Run(Prelude + source);
        run.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        run.CompileErrors.Should().BeEmpty(run.GeneratedSource);
        run.Assembly.Should().NotBeNull();
        return run;
    }

    internal static string Options(string mode, string extra = "") =>
        $"[DeepEqualsSourceGenerationOptions(CycleHandling = DeepEqualsCycleHandling.{mode}{(extra.Length > 0 ? ", " + extra : string.Empty)})]";

    internal static void Set(object target, string member, object? value) => target.GetType().GetField(member)!.SetValue(target, value);

    internal static object? Get(object target, string member) => target.GetType().GetField(member)!.GetValue(target);

    /// <summary>Calls a comparer and unwraps the reflection wrapper so the real exception surfaces.</summary>
    internal static T Unwrap<T>(Func<T> call)
    {
        try
        {
            return call();
        }
        catch (TargetInvocationException e) when (e.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e.InnerException).Throw();
            throw;
        }
    }

    // ----- shared models ----------------------------------------------------------------------------------------------

    internal const string TreeModel = """
        public sealed class TreeNode { public int Value; public List<TreeNode>? Children; }
        public sealed class Chain { public int Value; public Chain? Next; }
        public sealed class Forest { public List<Chain>? Chains; public TreeNode? Root; }
        """;

    internal static object Tree(GeneratorRun run, int value, params object[] children)
    {
        var node = run.New("TreeNode");
        Set(node, "Value", value);
        var list = (IList)Activator.CreateInstance(node.GetType().GetField("Children")!.FieldType)!;
        foreach (var child in children) list.Add(child);
        Set(node, "Children", list);
        return node;
    }

    /// <summary>A full tree of the given branching and depth: 1 + b + b^2 + ... nodes.</summary>
    internal static object FullTree(GeneratorRun run, int branching, int depth, int value = 0) =>
        depth == 0 ? Tree(run, value) : Tree(run, value, Enumerable.Range(0, branching).Select(i => FullTree(run, branching, depth - 1, value * branching + i + 1)).ToArray());

    internal static object ChainOf(GeneratorRun run, int length, int start = 0)
    {
        object? head = null;
        for (var i = length - 1; i >= 0; i--)
        {
            var node = run.New("Chain");
            Set(node, "Value", start + i);
            Set(node, "Next", head);
            head = node;
        }

        return head!;
    }

    // ----- Path ---------------------------------------------------------------------------------------------------------

    [Fact]
    public void Path_emits_a_rollback_for_every_guard()
    {
        var run = Clean(TreeModel + $$"""
                                    public struct S { public int V; public IEnumerable<S>? Children; }
                                    public sealed class Boxy { public object? Any; public (int, Boxy?) Pair; }
                                    [GenerateDeepEquals(typeof(Forest))]
                                    [GenerateDeepEquals(typeof(S))]
                                    [GenerateDeepEquals(typeof(Boxy))]
                                    {{Options("Path")}}
                                    public partial class Ctx : DeepEqualsContextBase { }
                                    """);

        var source = run.GeneratedSource;
        var enters = System.Text.RegularExpressions.Regex.Matches(source, @"state\.TryEnter\(").Count;
        var marks = System.Text.RegularExpressions.Regex.Matches(source, @"int pathMark = state\.Mark\(\);").Count;
        var rollbacks = System.Text.RegularExpressions.Regex.Matches(source, @"finally\s*\{\s*state\.Rollback\(pathMark\);").Count;
        enters.Should().BeGreaterThan(0);
        marks.Should().Be(enters, "every guard takes a mark");
        rollbacks.Should().Be(marks, "every mark is rolled back in a finally");
        source.Should().Contain("CycleHandling                = Path");
    }

    [Theory]
    [InlineData("Graph", 16, false)]
    [InlineData("Path", 16, true)]
    [InlineData("Graph", 1_000, true)]
    public void Path_retains_only_ancestors_so_a_wide_tree_fits_a_small_pair_budget(string mode, int budget, bool fits)
    {
        var run = Clean(TreeModel + $$"""
                                    [GenerateDeepEquals(typeof(TreeNode))]
                                    {{Options(mode, $"MaxComparisonPairs = {budget}")}}
                                    public partial class Ctx : DeepEqualsContextBase { }
                                    """);

        var a = FullTree(run, 8, 3);   // 585 nodes, depth 4
        var b = FullTree(run, 8, 3);
        var comparer = run.Comparer("Ctx", "TreeNode");
        var act = () => Unwrap(() => run.Equals(comparer, a, b));
        if (fits)
            act().Should().BeTrue();
        else
            act.Should().Throw<DeepEqualsComplexityException>("Graph retains one pair per node");
    }

    [Theory]
    [InlineData("Graph")]
    [InlineData("Path")]
    public void Graph_and_Path_terminate_on_cycles_and_equate_rolled_and_unrolled_forms(string mode)
    {
        var run = Clean(TreeModel + $$"""
                                    [GenerateDeepEquals(typeof(Forest))]
                                    {{Options(mode)}}
                                    public partial class Ctx : DeepEqualsContextBase { }
                                    """);

        // A chain that loops on itself, against the same loop unrolled twice.
        var rolled = run.New("Chain");
        Set(rolled, "Value", 1);
        Set(rolled, "Next", rolled);
        var twoA = run.New("Chain");
        var twoB = run.New("Chain");
        Set(twoA, "Value", 1);
        Set(twoB, "Value", 1);
        Set(twoA, "Next", twoB);
        Set(twoB, "Next", twoA);
        var chains = run.Comparer("Ctx", "Chain");
        Unwrap(() => run.Equals(chains, rolled, twoA)).Should().BeTrue();
        run.Hash(chains, rolled).Should().Be(run.Hash(chains, twoA));

        // A node that holds itself as a child, against the same unrolled.
        var self = Tree(run, 1);
        ((IList)Get(self, "Children")!).Add(self);
        var inner = Tree(run, 1);
        var outer = Tree(run, 1, inner);
        ((IList)Get(inner, "Children")!).Add(outer);
        var trees = run.Comparer("Ctx", "TreeNode");
        Unwrap(() => run.Equals(trees, self, outer)).Should().BeTrue();
        run.Hash(trees, self).Should().Be(run.Hash(trees, outer));
        Unwrap(() => run.Equals(trees, self, Tree(run, 1, Tree(run, 2)))).Should().BeFalse();
    }

    [Theory]
    [InlineData("Graph")]
    [InlineData("Path")]
    public void Shared_subgraphs_are_correct_whether_compared_once_or_per_path(string mode)
    {
        var run = Clean(TreeModel + $$"""
                                    [GenerateDeepEquals(typeof(TreeNode))]
                                    {{Options(mode)}}
                                    public partial class Ctx : DeepEqualsContextBase { }
                                    """);

        // A diamond: both children of the root share one subtree. Path compares it once per path; Graph once.
        var shared = FullTree(run, 2, 3);
        var left = Tree(run, 0, Tree(run, 1, shared), Tree(run, 2, shared));
        var right = Tree(run, 0, Tree(run, 1, FullTree(run, 2, 3)), Tree(run, 2, FullTree(run, 2, 3)));
        var comparer = run.Comparer("Ctx", "TreeNode");
        Unwrap(() => run.Equals(comparer, left, right)).Should().BeTrue();
        run.Hash(comparer, left).Should().Be(run.Hash(comparer, right));

        var changed = Tree(run, 0, Tree(run, 1, FullTree(run, 2, 3)), Tree(run, 2, FullTree(run, 2, 3, 100)));
        Unwrap(() => run.Equals(comparer, left, changed)).Should().BeFalse("the second path differs");
    }

    [Theory]
    [InlineData("Graph", false)]
    [InlineData("Path", true)]
    public void Path_tail_loops_roll_back_after_the_loop(string mode, bool fits)
    {
        var run = Clean(TreeModel + $$"""
                                    [GenerateDeepEquals(typeof(Forest))]
                                    {{Options(mode, "MaxComparisonPairs = 60")}}
                                    public partial class Ctx : DeepEqualsContextBase { }
                                    """);

        // A hundred chains of fifty: each loop's pairs are one path, gone before the next chain starts.
        object Forest()
        {
            var forest = run.New("Forest");
            var list = (IList)Activator.CreateInstance(forest.GetType().GetField("Chains")!.FieldType)!;
            for (var i = 0; i < 100; i++) list.Add(ChainOf(run, 50, i));
            Set(forest, "Chains", list);
            return forest;
        }

        var comparer = run.Comparer("Ctx", "Forest");
        var act = () => Unwrap(() => run.Equals(comparer, Forest(), Forest()));
        if (fits)
            act().Should().BeTrue();
        else
            act.Should().Throw<DeepEqualsComplexityException>("Graph keeps all 5,000 chain pairs");
    }

    [Theory]
    [InlineData("Graph")]
    [InlineData("Path")]
    public void Every_strategy_agrees_on_acyclic_data(string mode)
    {
        var run = Clean(TreeModel + $$"""
                                    public sealed class Mixed { public TreeNode? Tree; public Dictionary<string, Chain>? Named; public HashSet<Chain>? Set; public (int, Chain?) Pair; public object? Any; }
                                    [GenerateDeepEquals(typeof(Mixed))]
                                    [GenerateDeepEquals(typeof(Chain))]
                                    {{Options(mode)}}
                                    public partial class Ctx : DeepEqualsContextBase { }
                                    """);

        var chainType = run.Assembly!.GetTypes().Single(t => t.Name == "Chain");
        object Mixed(int variant)
        {
            var mixed = run.New("Mixed");
            Set(mixed, "Tree", FullTree(run, 3, 3, variant));
            var named = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), chainType))!;
            named["a"] = ChainOf(run, 5, variant);
            named["b"] = ChainOf(run, 3);
            Set(mixed, "Named", named);
            var set = Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(chainType))!;
            foreach (var i in Enumerable.Range(0, 20)) set.GetType().GetMethod("Add")!.Invoke(set, [ChainOf(run, 4, i)]);
            Set(mixed, "Set", set);
            Set(mixed, "Pair", Activator.CreateInstance(typeof(ValueTuple<,>).MakeGenericType(typeof(int), chainType), 7, ChainOf(run, 2)));
            Set(mixed, "Any", ChainOf(run, 3, variant));
            return mixed;
        }

        var comparer = run.Comparer("Ctx", "Mixed");
        Unwrap(() => run.Equals(comparer, Mixed(0), Mixed(0))).Should().BeTrue();
        run.Hash(comparer, Mixed(0)).Should().Be(run.Hash(comparer, Mixed(0)));
        Unwrap(() => run.Equals(comparer, Mixed(0), Mixed(1))).Should().BeFalse();
    }

    // ----- Tree ---------------------------------------------------------------------------------------------------------

    private static GeneratorRun TreeRun(string extra = "") => Clean(TreeModel + $$"""
        public sealed class SetNode { public int V; public HashSet<SetNode>? Items; }
        public sealed class A { public int V; public B? Next; }
        public sealed class B { public int V; public A? Next; }
        [GenerateDeepEquals(typeof(Forest))]
        [GenerateDeepEquals(typeof(SetNode))]
        [GenerateDeepEquals(typeof(A))]
        {{Options("Tree", extra)}}
        public partial class Ctx : DeepEqualsContextBase { }
        """);

    private static DeepEqualsComplexityException Throws(Action act) =>
        act.Should().Throw<DeepEqualsComplexityException>().Which;

    [Fact]
    public void Tree_emits_depth_parameters_and_no_state()
    {
        var run = TreeRun();
        var source = run.GeneratedSource;
        source.Should().NotContain("DeqState").And.NotContain("TryEnter").And.NotContain("try\n");
        source.Should().Contain("int depth").And.Contain("private const int MaxDepth = 512;");
        source.Should().Contain("if (++depth > MaxDepth)");
        source.Should().NotContain("ShallowHashCode_").And.NotContain("MatchHashCode_", "a Tree hash walks the whole value");
        source.Should().Contain("IDeepEqualsDepthElementOps<", "a set of a recursive type forwards the depth to its elements");
        source.Should().Contain("CycleHandling                = Tree").And.Contain("MaxComparisonPairs           = 1,000,000 (not used under Tree)");
    }

    [Fact]
    public void Tree_contract()
    {
        var run = TreeRun();
        var trees = run.Comparer("Ctx", "TreeNode");
        var chains = run.Comparer("Ctx", "Chain");

        // A node that is its own child.
        var loop = Tree(run, 1);
        ((IList)Get(loop, "Children")!).Add(loop);

        Unwrap(() => run.Equals(trees, loop, loop)).Should().BeTrue("the same reference is equal before any traversal");
        Throws(() => Unwrap(() => run.Hash(trees, loop))).TypeAtLimit.Should().Be(run.Assembly!.GetTypes().Single(t => t.Name == "TreeNode"), "a hash always traverses, and the guard that ran out is TreeNode's");

        var sharedA = Tree(run, 0, loop);
        var sharedB = Tree(run, 0, loop);
        Unwrap(() => run.Equals(trees, sharedA, sharedB)).Should().BeTrue("two roots sharing a cyclic child meet it as one reference");

        var early = Tree(run, 9, loop);
        Unwrap(() => run.Equals(trees, sharedA, early)).Should().BeFalse("an unequal member before the cycle decides first");

        var otherLoop = Tree(run, 1);
        ((IList)Get(otherLoop, "Children")!).Add(otherLoop);
        var ex = Throws(() => Unwrap(() => run.Equals(trees, loop, otherLoop)));
        ex.MaxDepth.Should().Be(512);
        ex.IsCycle.Should().BeFalse();

        var finite = FullTree(run, 1, 20, 1);
        Unwrap(() => run.Equals(trees, loop, finite)).Should().BeFalse("a finite side ends the walk");
        Unwrap(() => run.Equals(trees, finite, loop)).Should().BeFalse("in either operand order");

        // Two mutually recursive types close a cycle too.
        object MakeA(int v, object? next) { var a = run.New("A"); Set(a, "V", v); Set(a, "Next", next); return a; }
        object MakeB(int v, object? next) { var b = run.New("B"); Set(b, "V", v); Set(b, "Next", next); return b; }
        var a1 = MakeA(1, null);
        Set(a1, "Next", MakeB(2, a1));
        var a2 = MakeA(1, null);
        Set(a2, "Next", MakeB(2, a2));
        var mutual = Throws(() => Unwrap(() => run.Equals(run.Comparer("Ctx", "A"), a1, a2)));
        new[] { "A", "B" }.Should().Contain(mutual.TypeAtLimit!.Name);
        Unwrap(() => run.Equals(run.Comparer("Ctx", "A"), MakeA(1, MakeB(2, MakeA(3, null))), MakeA(1, MakeB(2, MakeA(3, null))))).Should().BeTrue();

        // A chain that loops: Brent's detector, not the depth bound.
        var rolled = run.New("Chain");
        Set(rolled, "Value", 1);
        Set(rolled, "Next", rolled);
        var rolled2 = run.New("Chain");
        Set(rolled2, "Value", 1);
        Set(rolled2, "Next", rolled2);
        var cycle = Throws(() => Unwrap(() => run.Equals(chains, rolled, rolled2)));
        cycle.IsCycle.Should().BeTrue();
        cycle.TypeAtLimit!.Name.Should().Be("Chain");
        Throws(() => Unwrap(() => run.Hash(chains, rolled))).IsCycle.Should().BeTrue();
        Unwrap(() => run.Equals(chains, rolled, ChainOf(run, 10, 1))).Should().BeFalse("a finite chain ends at its null");
        Unwrap(() => run.Equals(chains, ChainOf(run, 10, 1), rolled)).Should().BeFalse();
    }

    [Theory]
    [InlineData(3, 3, true)]
    [InlineData(3, 5, false)]
    [InlineData(64, 64, true)]
    [InlineData(64, 65, false)]
    public void Tree_max_depth_is_honoured(int maxDepth, int levels, bool passes)
    {
        var run = TreeRun($"MaxDepth = {maxDepth}");
        var a = FullTree(run, 1, levels - 1);
        var b = FullTree(run, 1, levels - 1);
        var trees = run.Comparer("Ctx", "TreeNode");
        if (passes)
        {
            Unwrap(() => run.Equals(trees, a, b)).Should().BeTrue();
            run.Hash(trees, a).Should().Be(run.Hash(trees, b));
        }
        else
        {
            Throws(() => Unwrap(() => run.Equals(trees, a, b))).MaxDepth.Should().Be(maxDepth);
            Throws(() => Unwrap(() => run.Hash(trees, a))).MaxDepth.Should().Be(maxDepth);
        }
    }

    [Fact]
    public void Tree_linked_lists_loop_without_growing_depth()
    {
        var run = TreeRun("MaxDepth = 8");
        var chains = run.Comparer("Ctx", "Chain");
        var a = ChainOf(run, 200_000);
        var b = ChainOf(run, 200_000);
        Unwrap(() => run.Equals(chains, a, b)).Should().BeTrue("a chain is walked in a loop, whatever MaxDepth says");
        run.Hash(chains, a).Should().Be(run.Hash(chains, b));
        var c = ChainOf(run, 200_000);
        var node = c;
        for (var i = 0; i < 150_000; i++) node = Get(node, "Next")!;
        Set(node, "Value", -1);
        Unwrap(() => run.Equals(chains, a, c)).Should().BeFalse();
        run.Hash(chains, a).Should().NotBe(run.Hash(chains, c), "the whole chain takes part in the hash");
    }

    [Fact]
    public void Tree_depth_is_carried_across_collections()
    {
        var run = TreeRun("MaxDepth = 10");
        var nodeType = run.Assembly!.GetTypes().Single(t => t.Name == "SetNode");
        object SetNode(int v, params object[] items)
        {
            var node = run.New("SetNode");
            Set(node, "V", v);
            var set = Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(nodeType))!;
            foreach (var item in items) set.GetType().GetMethod("Add")!.Invoke(set, [item]);
            Set(node, "Items", set);
            return node;
        }

        object Nested(int levels) => levels == 1 ? SetNode(levels) : SetNode(levels, Nested(levels - 1));
        var comparer = run.Comparer("Ctx", "SetNode");

        Unwrap(() => run.Equals(comparer, Nested(10), Nested(10))).Should().BeTrue("ten nodes deep, through nine sets, is within MaxDepth 10");
        run.Hash(comparer, Nested(10)).Should().Be(run.Hash(comparer, Nested(10)));
        Throws(() => Unwrap(() => run.Equals(comparer, Nested(11), Nested(11)))).MaxDepth.Should().Be(10);
        Throws(() => Unwrap(() => run.Hash(comparer, Nested(11)))).MaxDepth.Should().Be(10);

        // A node inside its own set: the set passes the depth on, so the budget cannot restart there.
        var self = SetNode(1);
        Get(self, "Items")!.GetType().GetMethod("Add")!.Invoke(Get(self, "Items"), [self]);
        var self2 = SetNode(1);
        Get(self2, "Items")!.GetType().GetMethod("Add")!.Invoke(Get(self2, "Items"), [self2]);
        Throws(() => Unwrap(() => run.Equals(comparer, self, self2)));
        Throws(() => Unwrap(() => run.Hash(comparer, self)));
    }

    [Fact]
    public void Tree_hash_walks_the_whole_tree()
    {
        using var _ = RawBitsTests.Seed(1);
        var tree = TreeRun();
        var graph = Clean(TreeModel + """
                                      [GenerateDeepEquals(typeof(TreeNode))]
                                      public partial class Ctx : DeepEqualsContextBase { }
                                      """);

        foreach (var (run, walksAll) in new[] { (tree, true), (graph, false) })
        {
            var comparer = run.Comparer("Ctx", "TreeNode");
            var a = Tree(run, 0, Tree(run, 1, Tree(run, 2, Tree(run, 3))));
            var b = Tree(run, 0, Tree(run, 1, Tree(run, 2, Tree(run, 4))));
            Unwrap(() => run.Equals(comparer, a, b)).Should().BeFalse();
            if (walksAll)
                run.Hash(comparer, a).Should().NotBe(run.Hash(comparer, b), "Tree sees three levels down");
            else
                run.Hash(comparer, a).Should().Be(run.Hash(comparer, b), "Graph's public hash looks one payload edge in");
        }
    }

    [Fact]
    public void Tree_boxed_struct_cycle_through_object_throws()
    {
        var run = Clean($$"""
                          public struct Cell { public int V; public object? Link; }
                          public sealed class Grid { public object? Start; }
                          [GenerateDeepEquals(typeof(Grid))]
                          [GenerateDeepEquals(typeof(Cell))]
                          {{Options("Tree")}}
                          public partial class Ctx : DeepEqualsContextBase { }
                          """);

        run.GeneratedSource.Should().Contain("DeqHelpers.Descend(depth, MaxDepth, typeof(global::Tests.Cell))", "the hash guards the boxed struct in its argument");
        var cellType = run.Assembly!.GetTypes().Single(t => t.Name == "Cell");
        object SelfCell()
        {
            var boxed = Activator.CreateInstance(cellType)!;
            cellType.GetField("V")!.SetValue(boxed, 1);
            cellType.GetField("Link")!.SetValue(boxed, boxed);
            return boxed;
        }

        var comparer = run.Comparer("Ctx", "Grid");
        var g1 = run.New("Grid"); Set(g1, "Start", SelfCell());
        var g2 = run.New("Grid"); Set(g2, "Start", SelfCell());
        Throws(() => Unwrap(() => run.Equals(comparer, g1, g2)));
        Throws(() => Unwrap(() => run.Hash(comparer, g1)));

        var flat = Activator.CreateInstance(cellType)!;
        cellType.GetField("V")!.SetValue(flat, 1);
        var g3 = run.New("Grid"); Set(g3, "Start", flat);
        var flat2 = Activator.CreateInstance(cellType)!;
        cellType.GetField("V")!.SetValue(flat2, 1);
        var g4 = run.New("Grid"); Set(g4, "Start", flat2);
        Unwrap(() => run.Equals(comparer, g3, g4)).Should().BeTrue();
        run.Hash(comparer, g3).Should().Be(run.Hash(comparer, g4));
    }

    [Fact]
    public void Tree_sets_and_dictionaries_of_recursive_types_compare_and_hash()
    {
        var run = Clean(TreeModel + $$"""
                                    public sealed class Holder { public HashSet<TreeNode>? Set; public Dictionary<string, Chain>? Map; public IEnumerable<TreeNode>? Seq; }
                                    [GenerateDeepEquals(typeof(Holder))]
                                    {{Options("Tree")}}
                                    public partial class Ctx : DeepEqualsContextBase { }
                                    """);

        run.GeneratedSource.Should().Contain("MaxUnorderedCollisionRun, depth)");
        var treeType = run.Assembly!.GetTypes().Single(t => t.Name == "TreeNode");
        var chainType = run.Assembly!.GetTypes().Single(t => t.Name == "Chain");
        object Holder(int variant)
        {
            var holder = run.New("Holder");
            var set = Activator.CreateInstance(typeof(HashSet<>).MakeGenericType(treeType))!;
            foreach (var i in Enumerable.Range(0, 30)) set.GetType().GetMethod("Add")!.Invoke(set, [FullTree(run, 2, 2, i + variant)]);
            Set(holder, "Set", set);
            var map = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(string), chainType))!;
            map["x"] = ChainOf(run, 5);
            Set(holder, "Map", map);
            var array = Array.CreateInstance(treeType, 2);
            array.SetValue(FullTree(run, 2, 1), 0);
            array.SetValue(FullTree(run, 2, 1, 5), 1);
            Set(holder, "Seq", typeof(Enumerable).GetMethod(nameof(Enumerable.Skip))!.MakeGenericMethod(treeType).Invoke(null, [array, 0]));
            return holder;
        }

        var comparer = run.Comparer("Ctx", "Holder");
        Unwrap(() => run.Equals(comparer, Holder(0), Holder(0))).Should().BeTrue();
        run.Hash(comparer, Holder(0)).Should().Be(run.Hash(comparer, Holder(0)));
        Unwrap(() => run.Equals(comparer, Holder(0), Holder(100))).Should().BeFalse();
    }

    [Theory]
    [InlineData("Graph")]
    [InlineData("Path")]
    [InlineData("Tree")]
    public void Every_mode_agrees_on_acyclic_forests(string mode)
    {
        var run = Clean(TreeModel + $$"""
                                    [GenerateDeepEquals(typeof(Forest))]
                                    {{Options(mode)}}
                                    public partial class Ctx : DeepEqualsContextBase { }
                                    """);

        object Forest(int variant)
        {
            var forest = run.New("Forest");
            var list = (IList)Activator.CreateInstance(forest.GetType().GetField("Chains")!.FieldType)!;
            for (var i = 0; i < 10; i++) list.Add(ChainOf(run, 20, i));
            Set(forest, "Chains", list);
            Set(forest, "Root", FullTree(run, 3, 3, variant));
            return forest;
        }

        var comparer = run.Comparer("Ctx", "Forest");
        Unwrap(() => run.Equals(comparer, Forest(0), Forest(0))).Should().BeTrue();
        run.Hash(comparer, Forest(0)).Should().Be(run.Hash(comparer, Forest(0)));
        Unwrap(() => run.Equals(comparer, Forest(0), Forest(1))).Should().BeFalse();
    }

    /// <summary>
    /// A struct whose children are an IEnumerable of itself: the struct body is never guarded, so the sequence keeps the
    /// guard. With spans available but one side lazy, the sequence core must still compare the elements.
    /// </summary>
    [Theory]
    [InlineData("Graph")]
    [InlineData("Path")]
    [InlineData("Tree")]
    public void Guarded_sequence_without_spans_on_both_sides_still_compares_its_elements(string mode)
    {
        var run = Clean($$"""
                          public struct S { public int V; public IEnumerable<S>? Children; }
                          [GenerateDeepEquals(typeof(S))]
                          {{Options(mode)}}
                          public partial class Ctx : DeepEqualsContextBase { }
                          """);

        run.GeneratedSource.Should().Contain(mode == "Tree" ? "typeof(global::System.Collections.Generic.IEnumerable<global::Tests.S>)" : "Kind_IEnumerableOfS", "the sequence holds the cycle's only guard");
        var sType = run.Assembly!.GetTypes().Single(t => t.Name == "S");
        object S(int v, IEnumerable? children)
        {
            var s = Activator.CreateInstance(sType)!;
            Set(s, "V", v);
            Set(s, "Children", children);
            return s;
        }

        IEnumerable Lazy(params object[] items)
        {
            var array = Array.CreateInstance(sType, items.Length);
            for (var i = 0; i < items.Length; i++) array.SetValue(items[i], i);
            return (IEnumerable)typeof(Enumerable).GetMethod(nameof(Enumerable.Skip))!.MakeGenericMethod(sType).Invoke(null, [array, 0])!;
        }

        var comparer = run.Comparer("Ctx", "S");
        var a = S(1, Lazy(S(2, null)));
        var b = S(1, Lazy(S(3, null)));
        run.Equals(comparer, a, b).Should().BeFalse("the children differ, and a lazy side has no span");
        run.Equals(comparer, a, S(1, Lazy(S(2, null)))).Should().BeTrue();
    }
}
