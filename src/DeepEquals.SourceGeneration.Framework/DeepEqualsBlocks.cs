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
using System.Security.Cryptography;

namespace DeepEquals.SourceGeneration.Framework;

/// <summary>
/// Sequences of bit-block elements: values whose equality is exactly the equality of their storage bytes, with no
/// references and no padding. Such a sequence is one contiguous block of bytes, so it compares with the runtime's
/// vectorized memory compare and hashes with seeded XxHash3 over those bytes, instead of one comparison and one hash
/// round per element.
/// </summary>
/// <remarks>
/// The hash of a bit-block sequence is defined as seeded XxHash3 over its bytes, folded to 32 bits, for every length
/// and every container shape, so an array, a list and a lazy sequence holding the same values hash alike. A shape
/// without a span is copied into a pooled buffer first. The result is never 0, which is reserved for null.
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

    private static long s_seed = GenerateSeed();

    /// <summary>The per-process XxHash3 seed. Settable from the test assembly only, so hash tests are deterministic.</summary>
    internal static long Seed
    {
        get => s_seed;
        set => s_seed = value;
    }

    private static long GenerateSeed()
    {
        var bytes = new byte[8];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
        return BitConverter.ToInt64(bytes, 0);
    }

    /// <summary>Seeded XxHash3 of a byte block, both halves folded to 32 bits; never 0.</summary>
    public static int HashBytes(ReadOnlySpan<byte> bytes)
    {
        var hash = XxHash3.HashToUInt64(bytes, s_seed);
        var folded = unchecked((int)(hash ^ (hash >> 32)));
        return folded == 0 ? 1 : folded;
    }

    /// <summary>The hash of a span of bit-block elements.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int HashBlock<T>(ReadOnlySpan<T> items) where T : unmanaged => HashBytes(MemoryMarshal.AsBytes(items));

    /// <summary>The hash of a read-only list of bit-block elements: its span when it has one, otherwise a copy.</summary>
    public static int HashReadOnlyList<T>(IReadOnlyList<T> list) where T : unmanaged
    {
        if (TryGetSpan(list, out var span))
            return HashBlock(span);

        var count = list.Count;
        return count <= StackCopyBytes / Unsafe.SizeOf<T>()
            ? HashSmall(list, count)
            : HashRented(list, count);
    }

    /// <summary>The hash of a list of bit-block elements.</summary>
    public static int HashList<T>(IList<T> list) where T : unmanaged
    {
        if (TryGetSpan(list, out var span))
            return HashBlock(span);

        var count = list.Count;
        var pool = DeepEqualsPools<T>.Shared;
        var buffer = pool.Rent(Math.Max(count, 1));
        try
        {
            list.CopyTo(buffer, 0);
            return HashBlock(new ReadOnlySpan<T>(buffer, 0, count));
        }
        finally
        {
            pool.Return(buffer);   // unmanaged elements hold no references, so the pool needs no clearing
        }
    }

    /// <summary>
    /// The hash of any sequence of bit-block elements: its span when it has one, otherwise the elements it enumerates,
    /// copied into a pooled buffer that grows as needed. An advertised count only sizes the first buffer; the hash is of
    /// what the enumerator yields.
    /// </summary>
    public static int HashEnumerable<T>(IEnumerable<T> items) where T : unmanaged
    {
        if (TryGetSpan(items, out var span))
            return HashBlock(span);

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

            return HashBlock(new ReadOnlySpan<T>(buffer, 0, count));
        }
        finally
        {
            pool.Return(buffer);
        }
    }

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
    private static int HashSmall<T>(IReadOnlyList<T> list, int count) where T : unmanaged
    {
        Span<T> items = stackalloc T[count];
        for (var i = 0; i < count; i++)
            items[i] = list[i];

        return HashBlock((ReadOnlySpan<T>)items);
    }

    private static int HashRented<T>(IReadOnlyList<T> list, int count) where T : unmanaged
    {
        var pool = DeepEqualsPools<T>.Shared;
        var buffer = pool.Rent(count);
        try
        {
            for (var i = 0; i < count; i++)
                buffer[i] = list[i];

            return HashBlock(new ReadOnlySpan<T>(buffer, 0, count));
        }
        finally
        {
            pool.Return(buffer);
        }
    }
}
#endif
