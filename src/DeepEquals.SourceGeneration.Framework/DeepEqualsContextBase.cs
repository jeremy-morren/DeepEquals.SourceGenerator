using System;
using System.Collections.Generic;
using DeepEquals.SourceGeneration.Framework;

namespace DeepEquals.SourceGeneration;

/// <summary>Base class of every generated deep-equality context. Holds no instance state.</summary>
public abstract class DeepEqualsContextBase
{
    protected DeepEqualsContextBase()
    {
    }

    /// <summary>
    /// Looks a comparer up by exact type. Never throws, so a generic static cache initializer can store the result or null
    /// without poisoning the cache type; <see cref="RequireEqualityComparer{T}"/> turns null into the documented exception.
    /// </summary>
    protected static object? LookupEqualityComparer(Dictionary<Type, object> comparers, Type type)
    {
        if (comparers is null) throw new ArgumentNullException(nameof(comparers));
        return comparers.TryGetValue(type, out object? comparer) ? comparer : null;
    }

    /// <summary>Returns the cached comparer or throws <see cref="DeepEqualsMissingComparerException"/> for <typeparamref name="T"/>.</summary>
    protected static IEqualityComparer<T> RequireEqualityComparer<T>(object? comparer)
        => comparer as IEqualityComparer<T> ?? throw new DeepEqualsMissingComparerException(typeof(T));
}
