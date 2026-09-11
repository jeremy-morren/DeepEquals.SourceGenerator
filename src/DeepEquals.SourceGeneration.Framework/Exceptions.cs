// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

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

    /// <summary>
    /// Under <c>CycleHandling.Tree</c>, a traversal passed the <c>MaxDepth</c> bound at a guard of <paramref name="type"/>.
    /// The type is where the depth ran out, not necessarily the one that closes a cycle.
    /// </summary>
    public DeepEqualsComplexityException(Type type, int maxDepth)
        : base($"Comparing or hashing nested deeper than MaxDepth {maxDepth} at '{type}'. Under CycleHandling = Tree the traversal is bounded, not the input validated: either the value is nested this deep, or it holds a cycle the traversal entered. Raise MaxDepth, or use CycleHandling = Graph or Path for in-memory graphs with cycles.")
    {
        TypeAtLimit = type;
        MaxDepth = maxDepth;
    }

    /// <summary>Under <c>CycleHandling.Tree</c>, the chain of <paramref name="type"/> a linked-list loop was walking came back to a node it had passed.</summary>
    public DeepEqualsComplexityException(Type type)
        : base($"The chain of '{type}' being compared or hashed loops back on itself. Under CycleHandling = Tree a cycle the traversal enters throws; use CycleHandling = Graph or Path for in-memory graphs with cycles.")
    {
        TypeAtLimit = type;
        IsCycle = true;
    }

    /// <summary>The type whose guard hit the depth bound or whose chain looped, or null for a pair-budget or collision-run failure.</summary>
    public Type? TypeAtLimit { get; }

    /// <summary>The configured depth bound for a depth failure, or 0 otherwise.</summary>
    public int MaxDepth { get; }

    /// <summary>True when a linked-list loop found a cycle.</summary>
    public bool IsCycle { get; }

    /// <summary>The collection whose collision run exceeded the cap, or null for a pair-budget failure.</summary>
    public Type? CollectionType { get; }

    /// <summary>The offending run length, or 0 for a pair-budget failure.</summary>
    public int RunLength { get; }

    /// <summary>The configured collision-run cap, or 0 for a pair-budget failure.</summary>
    public int Cap { get; }

    /// <summary>The retained pair count at the budget, or 0 for a collision-run failure.</summary>
    public int Pairs { get; }
}
