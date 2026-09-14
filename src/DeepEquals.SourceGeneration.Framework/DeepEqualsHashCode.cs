// Copyright 2026 The DeepEquals source generator project contributors. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
#if NETCOREAPP3_0_OR_GREATER
using System.Numerics;
#endif

// ReSharper disable InconsistentNaming

namespace DeepEquals.SourceGeneration.Framework;

/// <summary>
/// Stateless value hashing for generated comparers: the xxHash32 stream that <c>System.HashCode</c> uses,
/// with a random per-process seed, fixed-arity <c>Combine</c> overloads over already computed hashes,
/// seeded leaf hashes for values wider than 32 bits, and two streaming forms for collections.
/// </summary>
public static partial class DeepEqualsHashCode
{
    private const uint Prime1 = 2654435761U;
    private const uint Prime2 = 2246822519U;
    private const uint Prime3 = 3266489917U;
    private const uint Prime4 = 668265263U;
    private const uint Prime5 = 374761393U;

    private static uint s_seed = unchecked((uint)RandomSeed());

    /// <summary>The per-process seed. Settable from the test assembly only, so hash tests are deterministic.</summary>
    internal static uint Seed
    {
        get => s_seed;
        set => s_seed = value;
    }

    /// <summary>Eight bytes from the operating system's cryptographic generator: every per-process hash seed starts here.</summary>
    internal static ulong RandomSeed()
    {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt64(bytes);
#else
        var bytes = new byte[8];
        using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(bytes);
        return BitConverter.ToUInt64(bytes, 0);
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint RotateLeft(uint value, int offset)
    {
#if NETCOREAPP3_0_OR_GREATER
        return BitOperations.RotateLeft(value, offset);
#else
        return (value << offset) | (value >> (32 - offset));
#endif
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint Round(uint hash, uint input) => RotateLeft(hash + input * Prime2, 13) * Prime1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint QueueRound(uint hash, uint queuedValue) => RotateLeft(hash + queuedValue * Prime3, 17) * Prime4;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint MixState(uint v1, uint v2, uint v3, uint v4)
        => RotateLeft(v1, 1) + RotateLeft(v2, 7) + RotateLeft(v3, 12) + RotateLeft(v4, 18);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint MixFinal(uint hash)
    {
        hash ^= hash >> 15;
        hash *= Prime2;
        hash ^= hash >> 13;
        hash *= Prime3;
        hash ^= hash >> 16;
        return hash;
    }

    /// <summary>Maps the raw hash of an empty input to a nonzero value so that empty never collides with null's 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FinalizeEmptyZero(uint hash, int count) => count == 0 && hash == 0 ? 1 : unchecked((int)hash);

    // ----- Leaf hashes wider than 32 bits -----------------------------------------------------------------------------

    /// <summary>Seeded hash over both 32-bit halves. Never folds with xor, so collisions cannot be crafted from outside the process.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Hash(long value) => Combine(unchecked((int)value), unchecked((int)(value >> 32)));

    /// <summary>Seeded hash over both 32-bit halves.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Hash(ulong value) => Hash(unchecked((long)value));

    /// <summary>Seeded hash over all sixteen bytes; <see cref="Guid.GetHashCode"/> ignores six of them.</summary>
    public static int Hash(Guid value) => Hash128(ref value);

    /// <summary>Seeded hash over all four words of a decimal's storage, matching <see cref="DeepEqualsHelpers.DecimalEquals"/>.</summary>
    public static int Hash(in decimal value) => Hash128(ref Unsafe.AsRef(in value));

#if NET7_0_OR_GREATER
    /// <summary>Seeded hash over all four 32-bit words.</summary>
    public static int Hash(Int128 value) => Hash128(ref value);

    /// <summary>Seeded hash over all four 32-bit words.</summary>
    public static int Hash(UInt128 value) => Hash128(ref value);
#endif

    /// <summary>The seeded hash of the four 32-bit storage words of a 16-byte value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Hash128<T>(ref T value) where T : unmanaged =>
        Combine(DeepEqualsHelpers.Word(ref value, 0), DeepEqualsHelpers.Word(ref value, 1), DeepEqualsHelpers.Word(ref value, 2), DeepEqualsHelpers.Word(ref value, 3));

    /// <summary>
    /// Ordinal string hash: 0 for null, <see cref="string.GetHashCode()"/> where the runtime randomizes it, and a seeded
    /// Marvin implementation in the netstandard2.0 asset, where .NET Framework's non-randomized hash would let crafted keys
    /// hit the collision cap.
    /// </summary>
    public static int Hash(string? value)
    {
        if (value is null) return 0;
#if NETSTANDARD2_0
        return Marvin(value);
#else
        return value.GetHashCode();
#endif
    }

    // ----- Variable-length input --------------------------------------------------------------------------------------

    /// <summary>
    /// Hashes a span of elements through <typeparamref name="TOps"/> as one xxHash32 stream, four elements per round with
    /// no per-element bookkeeping. Equal to <see cref="Streaming"/> fed the same hashes and to <c>Combine</c> for 1 to 32 elements.
    /// </summary>
    public static int HashSpan<T, TOps>(ReadOnlySpan<T> items)
        where TOps : struct, IDeepEqualsHashOps<T>
        => HashStream<T, PlainOps<T, TOps>>(items, 0);

    /// <summary>
    /// <see cref="HashSpan{T, TOps}(ReadOnlySpan{T})"/> under <c>CycleHandling.Tree</c>: every element is hashed at the
    /// caller's <paramref name="depth"/>. The same stream, so equal to <see cref="Streaming"/> fed the same hashes.
    /// </summary>
    public static int HashSpan<T, TOps>(ReadOnlySpan<T> items, int depth)
        where TOps : struct, IDeepEqualsDepthHashOps<T>
        => HashStream<T, DepthOps<T, TOps>>(items, depth);

    /// <summary>The element hash a span stream calls, with or without a depth: the adapters below erase the difference.</summary>
    private interface ISpanOps<in T>
    {
        int GetHashCode(T value, int depth);
    }

    private readonly struct PlainOps<T, TOps> : ISpanOps<T>
        where TOps : struct, IDeepEqualsHashOps<T>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHashCode(T value, int depth) => default(TOps).GetHashCode(value);
    }

    private readonly struct DepthOps<T, TOps> : ISpanOps<T>
        where TOps : struct, IDeepEqualsDepthHashOps<T>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHashCode(T value, int depth) => default(TOps).GetHashCode(value, depth);
    }

    private static int HashStream<T, TW>(ReadOnlySpan<T> items, int depth)
        where TW : struct, ISpanOps<T>
    {
        unchecked
        {
            var seed = s_seed;
            var length = items.Length;
            uint hash;
            var i = 0;
            if (length >= 4)
            {
                var v1 = seed + Prime1 + Prime2;
                var v2 = seed + Prime2;
                var v3 = seed;
                var v4 = seed - Prime1;
                for (; i + 4 <= length; i += 4)
                {
                    v1 = Round(v1, (uint)default(TW).GetHashCode(items[i], depth));
                    v2 = Round(v2, (uint)default(TW).GetHashCode(items[i + 1], depth));
                    v3 = Round(v3, (uint)default(TW).GetHashCode(items[i + 2], depth));
                    v4 = Round(v4, (uint)default(TW).GetHashCode(items[i + 3], depth));
                }
                hash = MixState(v1, v2, v3, v4);
            }
            else hash = seed + Prime5;

            hash += (uint)length * 4;
            for (; i < length; i++) hash = QueueRound(hash, (uint)default(TW).GetHashCode(items[i], depth));

            return FinalizeEmptyZero(MixFinal(hash), length);
        }
    }

    /// <summary>
    /// Streaming form for indexer loops and enumerators: the same xxHash32 stream as the fixed-arity overloads, paying a
    /// position switch per <see cref="Add"/>. Declare as a local, add every element hash in order, then call <see cref="ToHashCode"/>.
    /// </summary>
    public ref struct Streaming
    {
        private uint _v1;
        private uint _v2;
        private uint _v3;
        private uint _v4;
        private uint _queue1;
        private uint _queue2;
        private uint _queue3;
        private uint _length;

        /// <summary>Adds one element hash to the stream.</summary>
        public void Add(int hash)
        {
            unchecked
            {
                var value = (uint)hash;
                var previousLength = _length++;
                var position = previousLength % 4;
                if (position == 0) _queue1 = value;
                else if (position == 1) _queue2 = value;
                else if (position == 2) _queue3 = value;
                else
                {
                    if (previousLength == 3)
                    {
                        var seed = s_seed;
                        _v1 = seed + Prime1 + Prime2;
                        _v2 = seed + Prime2;
                        _v3 = seed;
                        _v4 = seed - Prime1;
                    }

                    _v1 = Round(_v1, _queue1);
                    _v2 = Round(_v2, _queue2);
                    _v3 = Round(_v3, _queue3);
                    _v4 = Round(_v4, value);
                }
            }
        }

        /// <summary>Closes the stream. Empty input maps to a nonzero value.</summary>
        public int ToHashCode()
        {
            unchecked
            {
                var length = _length;
                var position = length % 4;
                var hash = length < 4 ? s_seed + Prime5 : MixState(_v1, _v2, _v3, _v4);
                hash += length * 4;
                if (position > 0)
                {
                    hash = QueueRound(hash, _queue1);
                    if (position > 1)
                    {
                        hash = QueueRound(hash, _queue2);
                        if (position > 2) hash = QueueRound(hash, _queue3);
                    }
                }

                return FinalizeEmptyZero(MixFinal(hash), (int)length);
            }
        }
    }
}
