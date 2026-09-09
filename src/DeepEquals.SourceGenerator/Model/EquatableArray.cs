// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace DeepEquals.SourceGenerator.Model;

/// <summary>An immutable array with structural equality, for sequences inside the incremental model.</summary>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
{
    private readonly T[]? _items;

    public EquatableArray(T[] items)
    {
        _items = items.Length == 0 ? null : items;
    }

    public EquatableArray(List<T> items)
        : this(items.ToArray())
    {
    }

    public static EquatableArray<T> Empty => default;

    public int Count => _items?.Length ?? 0;

    public T this[int index] => _items![index];

    public T[] ToArray() => _items is null ? [] : (T[])_items.Clone();

    public bool Equals(EquatableArray<T> other)
    {
        var a = _items;
        var b = other._items;
        if (ReferenceEquals(a, b)) 
            return true;

        if (a is null || b is null || a.Length != b.Length) 
            return false;

        var comparer = EqualityComparer<T>.Default;
        return Enumerable.Range(0, a.Length).All(i => comparer.Equals(a[i], b[i]));
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_items is null) 
            return 0;

        unchecked
        {
            var comparer = EqualityComparer<T>.Default;
            return _items.Aggregate(17, 
                (current, item) => current * 31 + (item is null ? 0 : comparer.GetHashCode(item)));
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? [])).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}

internal static class EquatableArray
{
    public static EquatableArray<T> Create<T>(List<T> items) => new(items);

    public static EquatableArray<T> Create<T>(params T[] items) => new(items);
}
