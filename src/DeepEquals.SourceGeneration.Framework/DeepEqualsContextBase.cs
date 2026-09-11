// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using DeepEquals.SourceGeneration.Framework;

// ReSharper disable once CheckNamespace
namespace DeepEquals.SourceGeneration;

/// <summary>Base class of every generated deep-equality context. Holds no instance state.</summary>
public abstract class DeepEqualsContextBase
{
    /// <summary>
    /// Looks a comparer up by exact type.
    /// Never throws, so a generic static cache initializer can store the result or null without poisoning the cache type;
    /// <see cref="RequireEqualityComparer{T}(object?)"/> turns null into the documented exception.
    /// </summary>
    protected static object? LookupEqualityComparer(Dictionary<Type, object> comparers, Type type)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(comparers);
#else
        if (comparers is null) throw new ArgumentNullException(nameof(comparers));
#endif
#if !NETSTANDARD2_0
        return comparers.GetValueOrDefault(type);
#else
        return comparers.TryGetValue(type, out var comparer) ? comparer : null;
#endif
    }

    /// <summary>
    /// Looks a comparer up by exact type, already typed for the generic static cache that stores it, so the call that
    /// reads the cache needs no cast. Never throws.
    /// </summary>
    protected static IEqualityComparer<T>? LookupEqualityComparer<T>(Dictionary<Type, object> comparers)
        => LookupEqualityComparer(comparers, typeof(T)) as IEqualityComparer<T>;

    /// <summary>Returns the cached comparer or throws <see cref="DeepEqualsMissingComparerException"/> for <typeparamref name="T"/>.</summary>
    protected static IEqualityComparer<T> RequireEqualityComparer<T>(object? comparer)
        => comparer as IEqualityComparer<T> ?? throw new DeepEqualsMissingComparerException(typeof(T));

    /// <summary>Returns the cached comparer or throws <see cref="DeepEqualsMissingComparerException"/> for <typeparamref name="T"/>.</summary>
    protected static IEqualityComparer<T> RequireEqualityComparer<T>(IEqualityComparer<T>? comparer)
        => comparer ?? throw new DeepEqualsMissingComparerException(typeof(T));
}
