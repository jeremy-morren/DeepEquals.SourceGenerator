// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

namespace DeepEquals.SourceGenerator.Model;

/// <summary>The six per-context knobs after merging the context chain and applying defaults. Defaults duplicate the framework attribute's.</summary>
internal sealed record ContextOptions(
    int MaxSwitchCases,
    int MaxUnorderedCollisionRun,
    int MaxComparisonPairs,
    int MaxBinaryExpressionArity,
    int StructPassByValueMaxByteSize,
    EquatableArray<string> ExcludeInterfacesByPrefix)
{
    public const int DefaultMaxSwitchCases = 12;
    public const int DefaultMaxUnorderedCollisionRun = 64;
    public const int MaximumMaxUnorderedCollisionRun = 512;
    public const int DefaultMaxComparisonPairs = 1_000_000;
    public const int MaximumMaxComparisonPairs = 1 << 29;
    public const int DefaultMaxBinaryExpressionArity = 64;
    public const int DefaultStructPassByValueMaxByteSize = 8;

    public static ContextOptions Default { get; } = new ContextOptions(
        DefaultMaxSwitchCases,
        DefaultMaxUnorderedCollisionRun,
        DefaultMaxComparisonPairs,
        DefaultMaxBinaryExpressionArity,
        DefaultStructPassByValueMaxByteSize,
        EquatableArray<string>.Empty);
}
