// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections;
using System.Collections.Generic;

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

    public bool IsEmpty => _items is null;

    public T[] ToArray() => _items is null ? Array.Empty<T>() : (T[])_items.Clone();

    public bool Equals(EquatableArray<T> other)
    {
        T[]? a = _items;
        T[]? b = other._items;
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null || a.Length != b.Length)
        {
            return false;
        }

        EqualityComparer<T> comparer = EqualityComparer<T>.Default;
        for (int i = 0; i < a.Length; i++)
        {
            if (!comparer.Equals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

    public override int GetHashCode()
    {
        if (_items is null)
        {
            return 0;
        }

        unchecked
        {
            int hash = 17;
            EqualityComparer<T> comparer = EqualityComparer<T>.Default;
            foreach (T item in _items)
            {
                hash = hash * 31 + (item is null ? 0 : comparer.GetHashCode(item));
            }

            return hash;
        }
    }

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? Array.Empty<T>())).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);
}

internal static class EquatableArray
{
    public static EquatableArray<T> Create<T>(List<T> items) => new EquatableArray<T>(items);

    public static EquatableArray<T> Create<T>(params T[] items) => new EquatableArray<T>(items);
}
