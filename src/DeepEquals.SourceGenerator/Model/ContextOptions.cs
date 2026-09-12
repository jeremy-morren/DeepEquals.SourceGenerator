// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

namespace DeepEquals.SourceGenerator.Model;

/// <summary>How a comparison remembers where it has been; mirrors the framework enum of the same name.</summary>
internal enum CycleHandling
{
    Graph = 0,
    Path = 1,
    Tree = 2,
}

/// <summary>The per-context knobs after merging the context chain and applying defaults. Defaults duplicate the framework attribute's.</summary>
internal sealed record ContextOptions(
    int MaxSwitchCases,
    int MaxUnorderedCollisionRun,
    int MaxComparisonPairs,
    int MaxBinaryExpressionArity,
    int StructPassByValueMaxByteSize,
    EquatableArray<string> ExcludeInterfacesByPrefix,
    CycleHandling CycleHandling,
    int MaxDepth,
    int MatchingHashDepth)
{
    public const int DefaultMaxSwitchCases = 12;
    public const int DefaultMaxUnorderedCollisionRun = 64;
    public const int MaximumMaxUnorderedCollisionRun = 512;
    public const int DefaultMaxComparisonPairs = 1_000_000;
    public const int MaximumMaxComparisonPairs = 1 << 29;
    public const int DefaultMaxBinaryExpressionArity = 64;
    public const int DefaultStructPassByValueMaxByteSize = 8;
    public const int DefaultMaxDepth = 512;
    public const int MaximumMaxDepth = 1_000_000;
    public const int DefaultMatchingHashDepth = 4;
    public const int MaximumMatchingHashDepth = 16;

    public static ContextOptions Default { get; } = new(
        DefaultMaxSwitchCases,
        DefaultMaxUnorderedCollisionRun,
        DefaultMaxComparisonPairs,
        DefaultMaxBinaryExpressionArity,
        DefaultStructPassByValueMaxByteSize,
        EquatableArray<string>.Empty,
        CycleHandling.Graph,
        DefaultMaxDepth,
        DefaultMatchingHashDepth);

    public bool IsTree => CycleHandling == CycleHandling.Tree;

    public bool IsPath => CycleHandling == CycleHandling.Path;
}
