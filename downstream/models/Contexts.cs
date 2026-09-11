// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

// ReSharper disable All

using System.Collections.Generic;
using DeepEquals.SourceGeneration;

namespace DeepEquals.Downstream
{
    /// <summary>
    /// The one context every downstream project declares, so the generator runs in each of them against the packed
    /// package: the smoke consumers check what it emits, the benchmarks time it.
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
#if RECORDS
    [GenerateDeepEquals(typeof(Invoice))]
    [GenerateDeepEquals(typeof(Person))]
    [GenerateDeepEquals(typeof(Money))]
    [GenerateDeepEquals(typeof(Point3))]
#endif
    [SimpleType(typeof(SkuId))]
    public partial class DownstreamContext : DeepEqualsContextBase
    {
    }
}
