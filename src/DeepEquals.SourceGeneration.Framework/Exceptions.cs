using System;

namespace DeepEquals.SourceGeneration.Framework;

/// <summary>A dispatch core met a runtime type that no registration, member or shape in the context reached.</summary>
public sealed class DeepEqualsUnknownTypeException : InvalidOperationException
{
    public DeepEqualsUnknownTypeException(Type runtimeType)
        : base($"No deep-equality comparer was generated for runtime type '{runtimeType}'. Register it on the context with [GenerateDeepEquals], reach it through a member, or supply a [SimpleType]/[CustomEqualityComparer] rule.")
    {
        RuntimeType = runtimeType;
    }

    public Type RuntimeType { get; }
}

/// <summary><c>GetEqualityComparer&lt;T&gt;()</c> was asked for a type the context did not generate.</summary>
public sealed class DeepEqualsMissingComparerException : InvalidOperationException
{
    public DeepEqualsMissingComparerException(Type requestedType)
        : base($"The context has no comparer for '{requestedType}'. Register the type with [GenerateDeepEquals] or reach it through a registered type's members.")
    {
        RequestedType = requestedType;
    }

    public Type RequestedType { get; }
}

/// <summary>A comparison exceeded a documented bound: the retained-pair budget or the unordered collision-run cap.</summary>
public sealed class DeepEqualsComplexityException : InvalidOperationException
{
    /// <summary>A hash-collision run inside an unordered collection exceeded the cap.</summary>
    public DeepEqualsComplexityException(Type collectionType, int runLength, int cap)
        : base($"Comparing '{collectionType}' needed an exact matching over {runLength} entries sharing one hash, above the MaxUnorderedCollisionRun cap of {cap}.")
    {
        CollectionType = collectionType;
        RunLength = runLength;
        Cap = cap;
    }

    /// <summary>The number of distinct retained triples reached the pair budget.</summary>
    public DeepEqualsComplexityException(int pairs)
        : base($"The comparison retained {pairs} distinct object pairs, the MaxComparisonPairs budget, and needed another.")
    {
        Pairs = pairs;
    }

    /// <summary>The collection whose collision run exceeded the cap, or null for a pair-budget failure.</summary>
    public Type? CollectionType { get; }

    /// <summary>The offending run length, or 0 for a pair-budget failure.</summary>
    public int RunLength { get; }

    /// <summary>The configured collision-run cap, or 0 for a pair-budget failure.</summary>
    public int Cap { get; }

    /// <summary>The retained pair count at the budget, or 0 for a collision-run failure.</summary>
    public int Pairs { get; }
}
