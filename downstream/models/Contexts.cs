// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

// ReSharper disable All

using System;
using System.Collections.Generic;
using DeepEquals.SourceGeneration;

namespace DeepEquals.Downstream
{
    /// <summary>
    /// The one context every downstream project declares, so the generator runs in each of them against the packed
    /// package: the smoke consumers check what it emits, the benchmarks time it. Defaults throughout: Graph, XxHash32,
    /// fingerprint depth 4.
    /// </summary>
    [GenerateDeepEquals(typeof(GraphNode))]
    [GenerateDeepEquals(typeof(Customer))]
    [GenerateDeepEquals(typeof(Order))]
    [GenerateDeepEquals(typeof(Circle))]
    [GenerateDeepEquals(typeof(Square))]
    [GenerateDeepEquals(typeof(TreeNode))]
    [GenerateDeepEquals(typeof(Payload))]
    [GenerateDeepEquals(typeof(Dictionary<string, decimal>))]
    [GenerateDeepEquals(typeof(Dictionary<SkuId, int>))]
    [GenerateDeepEquals(typeof(IReadOnlyList<OrderLine>))]
    [GenerateDeepEquals(typeof(ListNode))]
    [GenerateDeepEquals(typeof(HashSet<ListNode>))]
    [GenerateDeepEquals(typeof(HashSet<TreeNode>))]
    [GenerateDeepEquals(typeof(Texts))]
    [GenerateDeepEquals(typeof(HashSet<CollidingId>))]
    [GenerateDeepEquals(typeof(Arrays))]
#if RECORDS
    [GenerateDeepEquals(typeof(Invoice))]
    [GenerateDeepEquals(typeof(Person))]
    [GenerateDeepEquals(typeof(Money))]
    [GenerateDeepEquals(typeof(Point3))]
    [GenerateDeepEquals(typeof(PointArrays))]
    [GenerateDeepEquals(typeof(IReadOnlyList<Point3>))]
#endif
    [SimpleType(typeof(SkuId))]
    [SimpleType(typeof(CollidingId))]
    public partial class DownstreamContext : DeepEqualsContextBase
    {
    }

    /// <summary>The same roots under the 64-bit hash stream, so every scenario can time both widths side by side.</summary>
    [GenerateDeepEquals(typeof(GraphNode))]
    [GenerateDeepEquals(typeof(Customer))]
    [GenerateDeepEquals(typeof(Order))]
    [GenerateDeepEquals(typeof(Circle))]
    [GenerateDeepEquals(typeof(Square))]
    [GenerateDeepEquals(typeof(TreeNode))]
    [GenerateDeepEquals(typeof(Payload))]
    [GenerateDeepEquals(typeof(Dictionary<string, decimal>))]
    [GenerateDeepEquals(typeof(Dictionary<SkuId, int>))]
    [GenerateDeepEquals(typeof(IReadOnlyList<OrderLine>))]
    [GenerateDeepEquals(typeof(ListNode))]
    [GenerateDeepEquals(typeof(HashSet<ListNode>))]
    [GenerateDeepEquals(typeof(HashSet<TreeNode>))]
    [GenerateDeepEquals(typeof(Texts))]
    [GenerateDeepEquals(typeof(HashSet<CollidingId>))]
    [GenerateDeepEquals(typeof(Arrays))]
#if RECORDS
    [GenerateDeepEquals(typeof(Invoice))]
    [GenerateDeepEquals(typeof(Person))]
    [GenerateDeepEquals(typeof(Money))]
    [GenerateDeepEquals(typeof(Point3))]
    [GenerateDeepEquals(typeof(PointArrays))]
    [GenerateDeepEquals(typeof(IReadOnlyList<Point3>))]
#endif
    [SimpleType(typeof(SkuId))]
    [SimpleType(typeof(CollidingId))]
    [DeepEqualsSourceGenerationOptions(Hashing = DeepEqualsHashing.XxHash64)]
    public partial class DownstreamHash64Context : DeepEqualsContextBase
    {
    }

    /// <summary>The recursive shapes retaining only the ancestors of the pair being compared.</summary>
    [GenerateDeepEquals(typeof(GraphNode))]
    [GenerateDeepEquals(typeof(TreeNode))]
    [GenerateDeepEquals(typeof(ListNode))]
    [GenerateDeepEquals(typeof(HashSet<TreeNode>))]
    [DeepEqualsSourceGenerationOptions(CycleHandling = DeepEqualsCycleHandling.Path)]
    public partial class DownstreamPathContext : DeepEqualsContextBase
    {
    }

    /// <summary>The recursive shapes with no pair table: one depth bound, for data that cannot hold cycles.</summary>
    [GenerateDeepEquals(typeof(GraphNode))]
    [GenerateDeepEquals(typeof(TreeNode))]
    [GenerateDeepEquals(typeof(ListNode))]
    [GenerateDeepEquals(typeof(HashSet<TreeNode>))]
    [DeepEqualsSourceGenerationOptions(CycleHandling = DeepEqualsCycleHandling.Tree)]
    public partial class DownstreamTreeContext : DeepEqualsContextBase
    {
    }

    /// <summary>The matching fingerprint at depth 1, the public hash, with the default collision cap: the audit's throwing case.</summary>
    [GenerateDeepEquals(typeof(HashSet<ListNode>))]
    [DeepEqualsSourceGenerationOptions(MatchingHashDepth = 1)]
    public partial class DownstreamMatch1Context : DeepEqualsContextBase
    {
    }

    /// <summary>
    /// The matching fingerprint at depth 1 with a cap wide enough to match every collision run exactly, and the colliding
    /// keys, so a hundred of them in one run can be timed rather than refused.
    /// </summary>
    [GenerateDeepEquals(typeof(HashSet<ListNode>))]
    [GenerateDeepEquals(typeof(HashSet<CollidingId>))]
    [SimpleType(typeof(CollidingId))]
    [DeepEqualsSourceGenerationOptions(MatchingHashDepth = 1, MaxUnorderedCollisionRun = 512)]
    public partial class DownstreamMatch1WideContext : DeepEqualsContextBase
    {
    }

    /// <summary>The matching fingerprint at depth 2.</summary>
    [GenerateDeepEquals(typeof(HashSet<ListNode>))]
    [DeepEqualsSourceGenerationOptions(MatchingHashDepth = 2)]
    public partial class DownstreamMatch2Context : DeepEqualsContextBase
    {
    }
}
