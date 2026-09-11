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
    [InlineData("Graph", "")]
    [InlineData("Path", "")]
    [InlineData("Graph", "Hashing = DeepEqualsHashing.XxHash64")]
    [InlineData("Path", "Hashing = DeepEqualsHashing.XxHash64")]
    public void Every_strategy_agrees_on_acyclic_data(string mode, string width)
    {
        var run = Clean(TreeModel + $$"""
                                    public sealed class Mixed { public TreeNode? Tree; public Dictionary<string, Chain>? Named; public HashSet<Chain>? Set; public (int, Chain?) Pair; public object? Any; }
                                    [GenerateDeepEquals(typeof(Mixed))]
                                    [GenerateDeepEquals(typeof(Chain))]
                                    {{Options(mode, width)}}
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

    /// <summary>
    /// A struct whose children are an IEnumerable of itself: the struct body is never guarded, so the sequence keeps the
    /// guard. With spans available but one side lazy, the sequence core must still compare the elements.
    /// </summary>
    [Theory]
    [InlineData("Graph")]
    [InlineData("Path")]
    public void Guarded_sequence_without_spans_on_both_sides_still_compares_its_elements(string mode)
    {
        var run = Clean($$"""
                          public struct S { public int V; public IEnumerable<S>? Children; }
                          [GenerateDeepEquals(typeof(S))]
                          {{Options(mode)}}
                          public partial class Ctx : DeepEqualsContextBase { }
                          """);

        run.GeneratedSource.Should().Contain("Kind_IEnumerableOfS", "the sequence holds the cycle's only guard");
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
