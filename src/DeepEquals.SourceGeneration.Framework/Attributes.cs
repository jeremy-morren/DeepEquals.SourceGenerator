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
