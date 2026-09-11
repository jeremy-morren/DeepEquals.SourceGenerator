// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

// Absent from the netstandard2.1 asset, which serves netcoreapp3.1 to net5.0: System.IO.Hashing does not support them
// without a build warning. The generator probes for this type and keeps the per-element path where it is missing.
#if !NETSTANDARD2_1
using System;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace DeepEquals.SourceGeneration.Framework;

/// <summary>
/// Sequences of bit-block elements: values whose equality is exactly the equality of their storage bytes, with no
/// references and no padding. Such a sequence is one contiguous block of bytes, so it compares with the runtime's
/// vectorized memory compare and hashes with seeded XxHash3 over those bytes, instead of one comparison and one hash
/// round per element.
/// </summary>
/// <remarks>
/// The hash of a bit-block sequence is defined as XxHash3 over its bytes for every length and every container shape,
/// so an array, a list and a lazy sequence holding the same values hash alike. A shape without a span is copied into a
/// pooled buffer first. The result is never 0, which is reserved for null.
/// </remarks>
public static class DeepEqualsBlocks
{
    /// <summary>At most this many bytes are copied through the stack rather than a pooled array.</summary>
    private const int StackCopyBytes = 256;

    /// <summary>True when the runtime lays <typeparamref name="T"/> out in exactly <paramref name="size"/> bytes, the sum of its fields: no padding anywhere.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasSize<T>(int size) where T : unmanaged => Unsafe.SizeOf<T>() == size;

    // ----- Equality ---------------------------------------------------------------------------------------------------

    /// <summary>Bitwise equality of two sequences: equal lengths and equal bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool BlockEquals<T>(ReadOnlySpan<T> x, ReadOnlySpan<T> y) where T : unmanaged =>
        MemoryMarshal.AsBytes(x).SequenceEqual(MemoryMarshal.AsBytes(y));

    // ----- Hashing ----------------------------------------------------------------------------------------------------

    /// <summary>Seeded XxHash3 of a byte block, as a 64-bit word; never 0.</summary>
    public static ulong HashBytes64(ReadOnlySpan<byte> bytes)
    {
        var hash = XxHash3.HashToUInt64(bytes, unchecked((long)DeepEqualsHashCode64.Seed));
        return hash == 0 ? 1 : hash;
    }

    /// <summary>Seeded XxHash3 of a byte block, folded to 32 bits; never 0.</summary>
    public static int HashBytes32(ReadOnlySpan<byte> bytes) =>
        DeepEqualsHashCode64.FoldNonZero(XxHash3.HashToUInt64(bytes, unchecked((long)DeepEqualsHashCode64.Seed)));

    /// <summary>The 64-bit hash of a span of bit-block elements.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong HashBlock64<T>(ReadOnlySpan<T> items) where T : unmanaged => HashBytes64(MemoryMarshal.AsBytes(items));

    /// <summary>The 32-bit hash of a span of bit-block elements.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int HashBlock32<T>(ReadOnlySpan<T> items) where T : unmanaged => HashBytes32(MemoryMarshal.AsBytes(items));

    /// <summary>The 64-bit hash of a read-only list of bit-block elements: its span when it has one, otherwise a copy.</summary>
    public static ulong HashReadOnlyList64<T>(IReadOnlyList<T> list) where T : unmanaged
    {
        if (TryGetSpan(list, out var span))
            return HashBlock64(span);

        var count = list.Count;
        return count <= StackCopyBytes / Unsafe.SizeOf<T>()
            ? HashSmall64(list, count)
            : HashRented(list, count);
    }

    /// <summary>The 32-bit hash of a read-only list of bit-block elements.</summary>
    public static int HashReadOnlyList32<T>(IReadOnlyList<T> list) where T : unmanaged => Fold32(HashReadOnlyList64(list));

    /// <summary>The 64-bit hash of a list of bit-block elements.</summary>
    public static ulong HashList64<T>(IList<T> list) where T : unmanaged
    {
        if (TryGetSpan(list, out var span))
            return HashBlock64(span);

        var count = list.Count;
        var pool = DeepEqualsPools<T>.Shared;
        var buffer = pool.Rent(Math.Max(count, 1));
        try
        {
            list.CopyTo(buffer, 0);
            return HashBlock64(new ReadOnlySpan<T>(buffer, 0, count));
        }
        finally
        {
            pool.Return(buffer);   // unmanaged elements hold no references, so the pool needs no clearing
        }
    }

    /// <summary>The 32-bit hash of a list of bit-block elements.</summary>
    public static int HashList32<T>(IList<T> list) where T : unmanaged => Fold32(HashList64(list));

    /// <summary>
    /// The 64-bit hash of any sequence of bit-block elements: its span when it has one, otherwise the elements it
    /// enumerates, copied into a pooled buffer that grows as needed. An advertised count only sizes the first buffer;
    /// the hash is of what the enumerator yields.
    /// </summary>
    public static ulong HashEnumerable64<T>(IEnumerable<T> items) where T : unmanaged
    {
        if (TryGetSpan(items, out var span))
            return HashBlock64(span);

        var capacity = items is ICollection<T> c ? c.Count : items is IReadOnlyCollection<T> r ? r.Count : 16;
        var pool = DeepEqualsPools<T>.Shared;
        var buffer = pool.Rent(Math.Max(capacity, 1));
        var count = 0;
        try
        {
            foreach (var item in items)
            {
                if (count == buffer.Length)
                {
                    var larger = pool.Rent(checked(buffer.Length * 2));
                    Array.Copy(buffer, larger, count);
                    pool.Return(buffer);
                    buffer = larger;
                }

                buffer[count++] = item;
            }

            return HashBlock64(new ReadOnlySpan<T>(buffer, 0, count));
        }
        finally
        {
            pool.Return(buffer);
        }
    }

    /// <summary>The 32-bit hash of any sequence of bit-block elements.</summary>
    public static int HashEnumerable32<T>(IEnumerable<T> items) where T : unmanaged => Fold32(HashEnumerable64(items));

    /// <summary>A 64-bit block hash as the 32-bit width's word: the same fold <see cref="HashBytes32"/> applies.</summary>
    private static int Fold32(ulong hash) => DeepEqualsHashCode64.FoldNonZero(hash);

    private static bool TryGetSpan<T>(IEnumerable<T> source, out ReadOnlySpan<T> span)
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

    /// <summary>A short list copied through the stack: no pool traffic for the common small case.</summary>
    private static ulong HashSmall64<T>(IReadOnlyList<T> list, int count) where T : unmanaged
    {
        Span<T> items = stackalloc T[count];
        for (var i = 0; i < count; i++)
            items[i] = list[i];

        return HashBlock64((ReadOnlySpan<T>)items);
    }

    private static ulong HashRented<T>(IReadOnlyList<T> list, int count) where T : unmanaged
    {
        var pool = DeepEqualsPools<T>.Shared;
        var buffer = pool.Rent(count);
        try
        {
            for (var i = 0; i < count; i++)
                buffer[i] = list[i];

            return HashBlock64(new ReadOnlySpan<T>(buffer, 0, count));
        }
        finally
        {
            pool.Return(buffer);
        }
    }
}
#endif
