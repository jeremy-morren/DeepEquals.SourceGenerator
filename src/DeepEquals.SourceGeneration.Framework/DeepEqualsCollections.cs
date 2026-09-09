// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

#if !NETSTANDARD2_0
using System;
using System.Collections.Generic;

#if NET5_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace DeepEquals.SourceGeneration.Framework;

/// <summary>Span extraction for interface-typed collection views. Absent from the netstandard2.0 asset on purpose.</summary>
public static class DeepEqualsCollections
{
    /// <summary>
    /// Yields the contents of an array, or of a <see cref="List{T}"/> where <c>CollectionsMarshal</c> exists, as a read-only
    /// span. A covariant array is accepted because reading a derived element as its base is always sound.
    /// </summary>
    public static bool TryGetSpan<T>(IEnumerable<T>? source, out ReadOnlySpan<T> span)
    {
        if (source is T[] array)
        {
            span = array;
            return true;
        }

#if NET5_0_OR_GREATER
        if (source is List<T> list)
        {
            span = CollectionsMarshal.AsSpan(list);
            return true;
        }
#endif

        span = default;
        return false;
    }
}
#endif
