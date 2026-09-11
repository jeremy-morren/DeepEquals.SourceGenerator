// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using DeepEquals.SourceGeneration.Framework;

// ReSharper disable All

namespace DeepEquals.SourceGeneration;

/// <summary>Generates deep equality comparer for the specified type and all base types</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public sealed class GenerateDeepEqualsAttribute : Attribute
{
    public GenerateDeepEqualsAttribute(Type type)
    {
        Type = type;
    }

    public Type Type { get; }
}

/// <summary>Per-context generation options. Every setting defaults to the behaviour without the attribute; an empty attribute also marks a derived context.</summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
public sealed class DeepEqualsSourceGenerationOptionsAttribute : Attribute
{
    /// <summary>Default value for <see cref="MaxSwitchCases"/></summary>
    public const int DefaultMaxSwitchCases = 12;
    
    /// <summary>Default value for <see cref="MaxUnorderedCollisionRun"/></summary>
    public const int DefaultMaxUnorderedCollisionRun = 64;
    
    /// <summary>Maximum value for <see cref="MaxUnorderedCollisionRun"/></summary>
    public const int MaximumMaxUnorderedCollisionRun = 512;
    
    /// <summary>Default value for <see cref="MaxComparisonPairs"/></summary>
    public const int DefaultMaxComparisonPairs = 1_000_000;
    
    /// <summary>Maximum value for <see cref="MaxComparisonPairs"/></summary>
    public const int MaximumMaxComparisonPairs = DeepEqualsState.MaxPairBudget;
    
    /// <summary>Default value for <see cref="MaxBinaryExpressionArity"/></summary>
    public const int DefaultMaxBinaryExpressionArity = 64;
    
    /// <summary>Default value for <see cref="StructPassByValueMaxByteSize"/></summary>
    public const int DefaultStructPassByValueMaxByteSize = 8;

    /// <summary>Exact dispatch cases above which a dispatch core switches from an <c>if</c> chain to a dictionary-indexed <c>switch</c>.</summary>
    public int MaxSwitchCases { get; set; } = DefaultMaxSwitchCases;

    /// <summary>The longest run of equal-hash entries unordered matching will resolve exactly; longer runs throw.</summary>
    public int MaxUnorderedCollisionRun { get; set; } = DefaultMaxUnorderedCollisionRun;

    /// <summary>The number of distinct retained <c>(kind, x, y)</c> triples one comparison may hold.</summary>
    public int MaxComparisonPairs { get; set; } = DefaultMaxComparisonPairs;

    /// <summary>The largest generated <c>&amp;&amp;</c> predicate tree; longer member chains are split into consecutive chunks.</summary>
    public int MaxBinaryExpressionArity { get; set; } = DefaultMaxBinaryExpressionArity;

    /// <summary>Structs whose estimated size is at most this many bytes are passed by value; larger ones by <c>in</c>.</summary>
    public int StructPassByValueMaxByteSize { get; set; } = DefaultStructPassByValueMaxByteSize;

    /// <summary>Namespace prefixes whose interfaces the upward crawl skips; a prefix matches a namespace equal to it or starting with it plus a dot.</summary>
    public string[] ExcludeInterfacesByPrefix { get; set; } = [];

    /// <summary>Default value for <see cref="MaxDepth"/></summary>
    public const int DefaultMaxDepth = 512;

    /// <summary>Maximum value for <see cref="MaxDepth"/></summary>
    public const int MaximumMaxDepth = 1_000_000;

    /// <summary>Default value for <see cref="MatchingHashDepth"/></summary>
    public const int DefaultMatchingHashDepth = 4;

    /// <summary>Maximum value for <see cref="MatchingHashDepth"/></summary>
    public const int MaximumMatchingHashDepth = 16;

    /// <summary>
    /// How a comparison remembers where it has been, and therefore what a cyclic type costs. See
    /// <see cref="DeepEqualsCycleHandling"/> for what each value promises.
    /// </summary>
    public DeepEqualsCycleHandling CycleHandling { get; set; } = DeepEqualsCycleHandling.Graph;

    /// <summary>
    /// Under <see cref="DeepEqualsCycleHandling.Tree"/>: the guarded nesting depth past which a comparison or hash throws
    /// <c>DeepEqualsComplexityException</c>. A cycle detector, not a stack bound; linked lists are walked in a loop and do not
    /// count against it. Ignored under the other modes.
    /// </summary>
    public int MaxDepth { get; set; } = DefaultMaxDepth;

    /// <summary>
    /// Under <see cref="DeepEqualsCycleHandling.Graph"/> and <see cref="DeepEqualsCycleHandling.Path"/>: how many payload
    /// edges into a cycle the fingerprint used inside unordered matching follows, where the public hash follows one.
    /// A deeper fingerprint separates set entries that differ further into a recursive type, at a cost linear in the
    /// depth for chains and exponential for branching. Under <see cref="DeepEqualsCycleHandling.Tree"/> the fingerprint
    /// is the full hash and this option has no effect; setting it there reports <c>DEQ037</c>.
    /// </summary>
    public int MatchingHashDepth { get; set; } = DefaultMatchingHashDepth;

    /// <summary>The width of the hash stream every generated hash core runs. See <see cref="DeepEqualsHashing"/>.</summary>
    public DeepEqualsHashing Hashing { get; set; } = DeepEqualsHashing.XxHash32;
}

/// <summary>How a generated comparison remembers where it has been.</summary>
public enum DeepEqualsCycleHandling
{
    /// <summary>
    /// Every pair of objects entered at a cycle guard is retained for the whole comparison: a pair met again is taken as
    /// equal, and a shared subgraph is compared once. Handles real cycles and heavily shared graphs; each guarded pair costs
    /// a table probe. The default.
    /// </summary>
    Graph = 0,

    /// <summary>
    /// Only the ancestors of the current pair are retained: a pair leaves the table when its core returns. Handles real
    /// cycles; a shared subgraph is compared once per path that reaches it, which can be exponential on heavily shared
    /// graphs. The table stays small, so it rarely spills its inline slots.
    /// </summary>
    Path = 1,

    /// <summary>
    /// Nothing is retained: one depth counter bounds the traversal. No pair table, no pool rentals, no pair budget, and the
    /// hash walks the whole value instead of one level into a cycle. The traversal is bounded, not the input validated: a
    /// cycle the traversal enters throws once the depth passes <c>MaxDepth</c>, or at once from a linked-list loop; a cycle
    /// the traversal never reaches, because the roots are the same reference, a shared cyclic child is met, or an earlier
    /// member is unequal, does not. For deserialized data, which cannot hold cycles.
    /// </summary>
    Tree = 2,
}

/// <summary>The width of the hash stream generated hash cores run.</summary>
public enum DeepEqualsHashing
{
    /// <summary>xxHash32 over 32-bit words, the stream <c>System.HashCode</c> uses. For 32-bit processes. The default.</summary>
    XxHash32 = 0,

    /// <summary>
    /// xxHash64 over 64-bit words: a 64-bit leaf is one word, a 128-bit leaf two, and two 32-bit leaves pack into one, so
    /// a value takes about half the rounds. Nested hashes carry 64 bits; only the public result folds to 32. Its rounds
    /// are single instructions on 64-bit processors and in WebAssembly.
    /// </summary>
    XxHash64 = 1,
}

/// <summary>Treats the type, and every type assignable to it, as a leaf compared with <c>EqualityComparer&lt;T&gt;.Default</c> for the static type in use.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class SimpleTypeAttribute : Attribute
{
    public SimpleTypeAttribute(Type type)
    {
        Type = type;
    }

    public Type Type { get; }
}

/// <summary>Replaces all comparison of <c>T</c>, taken from the comparer's single <c>IEqualityComparer&lt;T&gt;</c>, with the given comparer.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class CustomEqualityComparerAttribute : Attribute
{
    /// <summary>Uses a public static <c>Instance</c> or <c>Default</c> member, otherwise a public parameterless constructor.</summary>
    public CustomEqualityComparerAttribute(Type equalityComparerType, bool handleNulls = false)
    {
        EqualityComparerType = equalityComparerType;
        HandleNulls = handleNulls;
    }

    /// <summary>Obtains the instance from the named public static field, property or parameterless method; abstract comparer classes are allowed.</summary>
    public CustomEqualityComparerAttribute(Type equalityComparerType, string staticMemberName, bool handleNulls = false)
    {
        EqualityComparerType = equalityComparerType;
        StaticMemberName = staticMemberName;
        HandleNulls = handleNulls;
    }

    public Type EqualityComparerType { get; }

    public string? StaticMemberName { get; }

    /// <summary>When true the comparer receives null too and owns the whole contract; when false the library null rule applies first.</summary>
    public bool HandleNulls { get; }
}

/// <summary>
/// Excludes storage from comparison. On a field, auto-property, <c>field</c>-keyword property or captured primary-constructor
/// parameter it excludes that storage; on the context class, the <c>(Type, string)</c> form names storage the user cannot annotate.
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class DeepEqualsIgnoreAttribute : Attribute
{
    public DeepEqualsIgnoreAttribute()
    {
    }

    /// <summary>Context-level form: the name is resolved as a field on <paramref name="declaringType"/> first, otherwise as a property whose compiler storage becomes the target.</summary>
    public DeepEqualsIgnoreAttribute(Type declaringType, string memberName)
    {
        DeclaringType = declaringType;
        MemberName = memberName;
    }

    public Type? DeclaringType { get; }

    public string? MemberName { get; }
}
