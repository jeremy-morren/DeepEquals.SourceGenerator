// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
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
/// The 64-bit counterpart of <see cref="DeepEqualsHashCode"/>: the xxHash64 stream over 64-bit words with its own
/// random per-process seed, fixed-arity <c>Combine</c> overloads over already computed words, single-word leaf hashes,
/// two streaming forms for collections, the packing of two 32-bit hashes into one word, and the fold to the 32-bit
/// value the public <c>GetHashCode</c> returns. A 64-bit leaf is one word, a 128-bit leaf two, so a value takes about
/// half the rounds of the 32-bit stream, and every round is one instruction on a 64-bit processor and in WebAssembly.
/// </summary>
public static partial class DeepEqualsHashCode64
{
    private const ulong Prime1 = 11400714785074694791UL;
    private const ulong Prime2 = 14029467366897019727UL;
    private const ulong Prime3 = 1609587929392839161UL;
    private const ulong Prime4 = 9650029242287828579UL;
    private const ulong Prime5 = 2870177450012600261UL;

    private static ulong s_seed = GenerateSeed();

    /// <summary>The per-process seed. Settable from the test assembly only, so hash tests are deterministic.</summary>
    internal static ulong Seed
    {
        get => s_seed;
        set => s_seed = value;
    }

    private static ulong GenerateSeed()
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
    private static ulong RotateLeft(ulong value, int offset)
    {
#if NETCOREAPP3_0_OR_GREATER
        return BitOperations.RotateLeft(value, offset);
#else
        return (value << offset) | (value >> (64 - offset));
#endif
    }

    /// <summary>One lane round: the xxHash64 accumulator step.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Round(ulong hash, ulong input) => RotateLeft(hash + input * Prime2, 31) * Prime1;

    /// <summary>Folds one lane back into the hash after the lanes are mixed.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MergeRound(ulong hash, ulong lane) => (hash ^ Round(0, lane)) * Prime1 + Prime4;

    /// <summary>The tail round for one remaining 8-byte word.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong QueueRound(ulong hash, ulong queuedValue) => RotateLeft(hash ^ Round(0, queuedValue), 27) * Prime1 + Prime4;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MixState(ulong v1, ulong v2, ulong v3, ulong v4)
    {
        var hash = RotateLeft(v1, 1) + RotateLeft(v2, 7) + RotateLeft(v3, 12) + RotateLeft(v4, 18);
        hash = MergeRound(hash, v1);
        hash = MergeRound(hash, v2);
        hash = MergeRound(hash, v3);
        hash = MergeRound(hash, v4);
        return hash;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong MixFinal(ulong hash)
    {
        hash ^= hash >> 33;
        hash *= Prime2;
        hash ^= hash >> 29;
        hash *= Prime3;
        hash ^= hash >> 32;
        return hash;
    }

    /// <summary>Maps the raw hash of an empty input to a nonzero value so that empty never collides with null's 0.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong FinalizeEmptyZero(ulong hash, int count) => count == 0 && hash == 0 ? 1 : hash;

    // ----- The 32-bit boundary ----------------------------------------------------------------------------------------

    /// <summary>The 32-bit value of a 64-bit hash: both halves take part.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Fold(ulong hash) => unchecked((int)(hash ^ (hash >> 32)));

    /// <summary>
    /// <see cref="Fold"/> for a collection root: a nonzero 64-bit hash can fold to zero, and the public hash of an empty
    /// collection must stay nonzero so that it never equals null's 0.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int FoldNonZero(ulong hash)
    {
        var folded = Fold(hash);
        return folded == 0 ? 1 : folded;
    }

    /// <summary>
    /// The value the public <c>GetHashCode</c> returns for a stream word: null's 0 stays 0, and every other word folds
    /// to a nonzero value, so an empty collection never equals null's hash however its 64-bit hash folds.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int ToInt32(ulong hash) => hash == 0 ? 0 : FoldNonZero(hash);

    /// <summary>Two 32-bit hashes as one word, the first in the high half. The casts go through <c>uint</c>, so a negative low half never sign-extends over the high one.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Pack(int high, int low) => ((ulong)(uint)high << 32) | (uint)low;

    /// <summary>One 32-bit hash as a word: zero-extended, never sign-extended.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Narrow(int hash) => (uint)hash;

    // ----- Leaf hashes ------------------------------------------------------------------------------------------------

    /// <summary>Seeded hash of one 64-bit word.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Hash(long value) => Combine(unchecked((ulong)value));

    /// <summary>Seeded hash of one 64-bit word.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Hash(ulong value) => Combine(value);

    /// <summary>Seeded hash over the eight bytes of a double, never its numeric value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Hash(double value) => Combine(DeepEqualsHelpers.DoubleBits(value));

    /// <summary>Seeded hash over both 64-bit halves of a Guid's storage.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Hash(in Guid value) => Combine(DeepEqualsHelpers.GuidLo64(value), DeepEqualsHelpers.GuidHi64(value));

    /// <summary>Seeded hash over both 64-bit halves of a decimal's storage, matching <see cref="DeepEqualsHelpers.DecimalEquals"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Hash(in decimal value) => Combine(DeepEqualsHelpers.DecimalLo64(value), DeepEqualsHelpers.DecimalHi64(value));

    /// <summary>Ordinal string hash as one word: 0 for null, otherwise the runtime's randomized hash or the seeded Marvin of the netstandard2.0 asset, zero-extended.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong Hash(string? value) => Narrow(DeepEqualsHashCode.Hash(value));

#if NET7_0_OR_GREATER
    /// <summary>Seeded hash over both 64-bit halves.</summary>
    public static ulong Hash(Int128 value)
    {
        ref var words = ref Unsafe.As<Int128, ulong>(ref value);
        return Combine(words, Unsafe.Add(ref words, 1));
    }

    /// <summary>Seeded hash over both 64-bit halves.</summary>
    public static ulong Hash(UInt128 value)
    {
        ref var words = ref Unsafe.As<UInt128, ulong>(ref value);
        return Combine(words, Unsafe.Add(ref words, 1));
    }
#endif

    // ----- Variable-length input --------------------------------------------------------------------------------------

#if !NETSTANDARD2_0
    /// <summary>
    /// Hashes a span of elements through <typeparamref name="TOps"/> as one xxHash64 stream, four elements per round with
    /// no per-element bookkeeping. Equal to <see cref="Streaming"/> fed the same words and to <c>Combine</c> for 1 to 32 elements.
    /// </summary>
    public static ulong HashSpan<T, TOps>(ReadOnlySpan<T> items)
        where TOps : struct, IDeepEqualsHashOps64<T>
    {
        unchecked
        {
            var seed = s_seed;
            var length = items.Length;
            ulong hash;
            var i = 0;
            if (length >= 4)
            {
                var v1 = seed + Prime1 + Prime2;
                var v2 = seed + Prime2;
                var v3 = seed;
                var v4 = seed - Prime1;
                for (; i + 4 <= length; i += 4)
                {
                    v1 = Round(v1, default(TOps).GetHashCode64(items[i]));
                    v2 = Round(v2, default(TOps).GetHashCode64(items[i + 1]));
                    v3 = Round(v3, default(TOps).GetHashCode64(items[i + 2]));
                    v4 = Round(v4, default(TOps).GetHashCode64(items[i + 3]));
                }
                hash = MixState(v1, v2, v3, v4);
            }
            else hash = seed + Prime5;

            hash += (ulong)length * 8;
            for (; i < length; i++) hash = QueueRound(hash, default(TOps).GetHashCode64(items[i]));

            return FinalizeEmptyZero(MixFinal(hash), length);
        }
    }

    /// <summary><see cref="HashSpan{T, TOps}(ReadOnlySpan{T})"/> under <c>CycleHandling.Tree</c>: every element is hashed at the caller's <paramref name="depth"/>.</summary>
    public static ulong HashSpan<T, TOps>(ReadOnlySpan<T> items, int depth)
        where TOps : struct, IDeepEqualsDepthHashOps64<T>
    {
        unchecked
        {
            var seed = s_seed;
            var length = items.Length;
            ulong hash;
            var i = 0;
            if (length >= 4)
            {
                var v1 = seed + Prime1 + Prime2;
                var v2 = seed + Prime2;
                var v3 = seed;
                var v4 = seed - Prime1;
                for (; i + 4 <= length; i += 4)
                {
                    v1 = Round(v1, default(TOps).GetHashCode64(items[i], depth));
                    v2 = Round(v2, default(TOps).GetHashCode64(items[i + 1], depth));
                    v3 = Round(v3, default(TOps).GetHashCode64(items[i + 2], depth));
                    v4 = Round(v4, default(TOps).GetHashCode64(items[i + 3], depth));
                }
                hash = MixState(v1, v2, v3, v4);
            }
            else hash = seed + Prime5;

            hash += (ulong)length * 8;
            for (; i < length; i++) hash = QueueRound(hash, default(TOps).GetHashCode64(items[i], depth));

            return FinalizeEmptyZero(MixFinal(hash), length);
        }
    }
#endif

    /// <summary>
    /// Streaming form for indexer loops and enumerators: the same xxHash64 stream as the fixed-arity overloads, paying a
    /// position switch per <see cref="Add(ulong)"/>. Declare as a local, add every element word in order, then call <see cref="ToHashCode"/>.
    /// </summary>
    public ref struct Streaming
    {
        private ulong _v1;
        private ulong _v2;
        private ulong _v3;
        private ulong _v4;
        private ulong _queue1;
        private ulong _queue2;
        private ulong _queue3;
        private uint _length;

        /// <summary>Adds one element word to the stream.</summary>
        public void Add(ulong value)
        {
            unchecked
            {
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

        /// <summary>Adds one 32-bit hash as a word.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(int hash) => Add(Narrow(hash));

        /// <summary>Closes the stream. Empty input maps to a nonzero value.</summary>
        public ulong ToHashCode()
        {
            unchecked
            {
                var length = _length;
                var position = length % 4;
                var hash = length < 4 ? s_seed + Prime5 : MixState(_v1, _v2, _v3, _v4);
                hash += (ulong)length * 8;
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
