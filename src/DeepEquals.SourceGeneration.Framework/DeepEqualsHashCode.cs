using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
#if NETCOREAPP3_0_OR_GREATER
using System.Numerics;
#endif

namespace DeepEquals.SourceGeneration.Framework;

/// <summary>
/// Stateless value hashing for generated comparers: the xxHash32 stream that <c>System.HashCode</c> uses, with a
/// random per-process seed, fixed-arity <c>Combine</c> overloads over already computed hashes, seeded leaf hashes for
/// values wider than 32 bits, and two streaming forms for collections.
/// </summary>
public static partial class DeepEqualsHashCode
{
    private const uint Prime1 = 2654435761U;
    private const uint Prime2 = 2246822519U;
    private const uint Prime3 = 3266489917U;
    private const uint Prime4 = 668265263U;
    private const uint Prime5 = 374761393U;

    private static uint s_seed = GenerateSeed();

    /// <summary>The per-process seed. Settable from the test assembly only, so hash tests are deterministic.</summary>
    internal static uint Seed
    {
        get => s_seed;
        set => s_seed = value;
    }

    private static uint GenerateSeed()
    {
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP
        Span<byte> bytes = stackalloc byte[4];
        RandomNumberGenerator.Fill(bytes);
        return BitConverter.ToUInt32(bytes);
#else
        byte[] bytes = new byte[4];
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return BitConverter.ToUInt32(bytes, 0);
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
    public static int Hash(Guid value)
    {
        ref int words = ref Unsafe.As<Guid, int>(ref value);
        return Combine(words, Unsafe.Add(ref words, 1), Unsafe.Add(ref words, 2), Unsafe.Add(ref words, 3));
    }

#if NET7_0_OR_GREATER
    /// <summary>Seeded hash over all four 32-bit words.</summary>
    public static int Hash(Int128 value)
    {
        ref int words = ref Unsafe.As<Int128, int>(ref value);
        return Combine(words, Unsafe.Add(ref words, 1), Unsafe.Add(ref words, 2), Unsafe.Add(ref words, 3));
    }

    /// <summary>Seeded hash over all four 32-bit words.</summary>
    public static int Hash(UInt128 value)
    {
        ref int words = ref Unsafe.As<UInt128, int>(ref value);
        return Combine(words, Unsafe.Add(ref words, 1), Unsafe.Add(ref words, 2), Unsafe.Add(ref words, 3));
    }
#endif

    /// <summary>
    /// Ordinal string hash: 0 for null, <see cref="string.GetHashCode()"/> where the runtime randomizes it, and a seeded
    /// Marvin implementation in the netstandard2.0 asset, where .NET Framework's non-randomized hash would let crafted keys
    /// hit the collision cap.
    /// </summary>
    public static int Hash(string? value)
    {
        if (value is null)
        {
            return 0;
        }
#if NETSTANDARD2_0
        return Marvin(value);
#else
        return value.GetHashCode();
#endif
    }

    // ----- Variable-length input --------------------------------------------------------------------------------------

#if !NETSTANDARD2_0
    /// <summary>
    /// Hashes a span of elements through <typeparamref name="TOps"/> as one xxHash32 stream, four elements per round with
    /// no per-element bookkeeping. Equal to <see cref="Streaming"/> fed the same hashes and to <c>Combine</c> for 1 to 32 elements.
    /// </summary>
    public static int HashSpan<T, TOps>(ReadOnlySpan<T> items)
        where TOps : struct, IDeepEqualsHashOps<T>
    {
        unchecked
        {
            uint seed = s_seed;
            int length = items.Length;
            uint hash;
            int i = 0;
            if (length >= 4)
            {
                uint v1 = seed + Prime1 + Prime2;
                uint v2 = seed + Prime2;
                uint v3 = seed;
                uint v4 = seed - Prime1;
                for (; i + 4 <= length; i += 4)
                {
                    v1 = Round(v1, (uint)default(TOps).GetHashCode(items[i]));
                    v2 = Round(v2, (uint)default(TOps).GetHashCode(items[i + 1]));
                    v3 = Round(v3, (uint)default(TOps).GetHashCode(items[i + 2]));
                    v4 = Round(v4, (uint)default(TOps).GetHashCode(items[i + 3]));
                }
                hash = MixState(v1, v2, v3, v4);
            }
            else
            {
                hash = seed + Prime5;
            }

            hash += (uint)length * 4;
            for (; i < length; i++)
            {
                hash = QueueRound(hash, (uint)default(TOps).GetHashCode(items[i]));
            }

            return FinalizeEmptyZero(MixFinal(hash), length);
        }
    }
#endif

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
                uint value = (uint)hash;
                uint previousLength = _length++;
                uint position = previousLength % 4;
                if (position == 0)
                {
                    _queue1 = value;
                }
                else if (position == 1)
                {
                    _queue2 = value;
                }
                else if (position == 2)
                {
                    _queue3 = value;
                }
                else
                {
                    if (previousLength == 3)
                    {
                        uint seed = s_seed;
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
                uint length = _length;
                uint position = length % 4;
                uint hash = length < 4 ? s_seed + Prime5 : MixState(_v1, _v2, _v3, _v4);
                hash += length * 4;
                if (position > 0)
                {
                    hash = QueueRound(hash, _queue1);
                    if (position > 1)
                    {
                        hash = QueueRound(hash, _queue2);
                        if (position > 2)
                        {
                            hash = QueueRound(hash, _queue3);
                        }
                    }
                }

                return FinalizeEmptyZero(MixFinal(hash), (int)length);
            }
        }
    }
}
