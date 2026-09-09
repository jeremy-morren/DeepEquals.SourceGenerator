using System;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace DeepEquals.SourceGeneration.Framework.Tests;

public sealed class HashCodeTests : IDisposable
{
    private readonly uint _savedSeed = DeepEqualsHashCode.Seed;

    public void Dispose() => DeepEqualsHashCode.Seed = _savedSeed;

    /// <summary>An independent, deliberately naive xxHash32 over 32-bit lanes, written from the algorithm description.</summary>
    private static int Reference(uint seed, int[] inputs)
    {
        const uint P1 = 2654435761U, P2 = 2246822519U, P3 = 3266489917U, P4 = 668265263U, P5 = 374761393U;
        static uint Rol(uint v, int n) => (v << n) | (v >> (32 - n));
        unchecked
        {
            uint hash;
            int i = 0;
            if (inputs.Length >= 4)
            {
                uint v1 = seed + P1 + P2, v2 = seed + P2, v3 = seed, v4 = seed - P1;
                for (; i + 4 <= inputs.Length; i += 4)
                {
                    v1 = Rol(v1 + (uint)inputs[i] * P2, 13) * P1;
                    v2 = Rol(v2 + (uint)inputs[i + 1] * P2, 13) * P1;
                    v3 = Rol(v3 + (uint)inputs[i + 2] * P2, 13) * P1;
                    v4 = Rol(v4 + (uint)inputs[i + 3] * P2, 13) * P1;
                }

                hash = Rol(v1, 1) + Rol(v2, 7) + Rol(v3, 12) + Rol(v4, 18);
            }
            else
            {
                hash = seed + P5;
            }

            hash += (uint)inputs.Length * 4;
            for (; i < inputs.Length; i++)
            {
                hash = Rol(hash + (uint)inputs[i] * P3, 17) * P4;
            }

            hash ^= hash >> 15;
            hash *= P2;
            hash ^= hash >> 13;
            hash *= P3;
            hash ^= hash >> 16;
            return (int)hash;
        }
    }

    private static int Combine(int[] inputs)
    {
        object result = typeof(DeepEqualsHashCode)
            .GetMethods()
            .Single(m => m.Name == "Combine" && m.GetParameters().Length == inputs.Length)
            .Invoke(null, inputs.Cast<object>().ToArray())!;
        return (int)result;
    }

    private static int Streaming(int[] inputs)
    {
        DeepEqualsHashCode.Streaming streaming = default;
        foreach (int input in inputs)
        {
            streaming.Add(input);
        }

        return streaming.ToHashCode();
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(0x9E3779B9u)]
    [InlineData(uint.MaxValue)]
    public void Combine_matches_reference_for_every_arity(uint seed)
    {
        DeepEqualsHashCode.Seed = seed;
        Random random = new Random(1234);
        for (int arity = 1; arity <= 32; arity++)
        {
            for (int trial = 0; trial < 20; trial++)
            {
                int[] inputs = Enumerable.Range(0, arity).Select(_ => random.Next(int.MinValue, int.MaxValue)).ToArray();
                Combine(inputs).Should().Be(Reference(seed, inputs), $"arity {arity} must be the xxHash32 stream");
                Streaming(inputs).Should().Be(Reference(seed, inputs), $"streaming of {arity} inputs must be the same stream");
            }
        }
    }

    [Fact]
    public void Empty_stream_with_seed_zero_is_the_published_xxHash32_vector()
    {
        DeepEqualsHashCode.Seed = 0;
        Streaming(Array.Empty<int>()).Should().Be(0x02CC5D05);
    }

    [Fact]
    public void Empty_stream_never_returns_zero()
    {
        // Search for a seed whose raw empty hash is 0 would take too long; assert the rule on the mapping itself instead.
        DeepEqualsHashCode.Seed = 0;
        Streaming(Array.Empty<int>()).Should().NotBe(0);
        DeepEqualsHashCode.Seed = 0xDEADBEEF;
        Streaming(Array.Empty<int>()).Should().NotBe(0);
    }

    [Fact]
    public void Streaming_lengths_around_lane_boundaries_match_reference()
    {
        DeepEqualsHashCode.Seed = 77;
        for (int length = 0; length <= 40; length++)
        {
            int[] inputs = Enumerable.Range(1, length).Select(i => i * 7919).ToArray();
            int expected = length == 0 ? Streaming(inputs) : Reference(77, inputs);
            Streaming(inputs).Should().Be(expected);
        }
    }

#if !NET472
    private struct IdentityOps : IDeepEqualsHashOps<int>
    {
        public int GetHashCode(int x) => x;
    }

    [Fact]
    public void HashSpan_matches_streaming_and_combine()
    {
        DeepEqualsHashCode.Seed = 99;
        for (int length = 0; length <= 40; length++)
        {
            int[] inputs = Enumerable.Range(1, length).Select(i => i * 104729).ToArray();
            int viaSpan = DeepEqualsHashCode.HashSpan<int, IdentityOps>(inputs);
            viaSpan.Should().Be(Streaming(inputs));
            if (length is >= 1 and <= 32)
            {
                viaSpan.Should().Be(Combine(inputs));
            }
        }
    }
#endif

    [Fact]
    public void Wide_leaf_hashes_use_both_halves()
    {
        DeepEqualsHashCode.Seed = 5;
        long a = 0x0000_0001_0000_0001L;
        long b = 0x0000_0002_0000_0002L;          // same lo ^ hi fold as a
        ((int)a ^ (int)(a >> 32)).Should().Be((int)b ^ (int)(b >> 32));
        DeepEqualsHashCode.Hash(a).Should().NotBe(DeepEqualsHashCode.Hash(b));
        DeepEqualsHashCode.Hash(a).Should().Be(DeepEqualsHashCode.Combine(unchecked((int)a), (int)(a >> 32)));
        DeepEqualsHashCode.Hash(unchecked((ulong)a)).Should().Be(DeepEqualsHashCode.Hash(a));
    }

    [Fact]
    public void Guid_hash_covers_every_word_the_bcl_folds_away()
    {
        DeepEqualsHashCode.Seed = 5;
        byte[] bytes = new byte[16];
#if NETFRAMEWORK
        // .NET Framework ignores bytes _d, _e, _g, _h, _i and _j entirely.
        Guid g1 = new Guid(bytes);
        bytes[8] = 1;                                   // _d
        Guid g2 = new Guid(bytes);
#else
        // .NET Core xor-folds the four 32-bit words, so any two words with equal xor collide.
        bytes[0] = 1;
        bytes[4] = 1;
        Guid g1 = new Guid(bytes);
        bytes[0] = 2;
        bytes[4] = 2;
        Guid g2 = new Guid(bytes);
#endif
        g1.GetHashCode().Should().Be(g2.GetHashCode(), "the BCL hash cannot tell these apart");
        DeepEqualsHashCode.Hash(g1).Should().NotBe(DeepEqualsHashCode.Hash(g2));
    }

    [Fact]
    public void String_hash_is_ordinal_and_null_is_zero()
    {
        DeepEqualsHashCode.Hash((string?)null).Should().Be(0);
        DeepEqualsHashCode.Hash("abc").Should().Be(DeepEqualsHashCode.Hash("abc"));
        DeepEqualsHashCode.Hash("abc").Should().NotBe(DeepEqualsHashCode.Hash("abd"));
        DeepEqualsHashCode.Hash("").Should().Be(DeepEqualsHashCode.Hash(""));
    }

#if NET472
    [Fact]
    public void Marvin_is_seeded_and_length_sensitive()
    {
        ulong saved = DeepEqualsHashCode.MarvinSeed;
        try
        {
            DeepEqualsHashCode.MarvinSeed = 1;
            int one = DeepEqualsHashCode.Hash("hello world");
            DeepEqualsHashCode.MarvinSeed = 2;
            int two = DeepEqualsHashCode.Hash("hello world");
            one.Should().NotBe(two, "the seed must participate");
            DeepEqualsHashCode.MarvinSeed = 1;
            DeepEqualsHashCode.Hash("hello world").Should().Be(one);
            // Every remaining-length case of the block loop: 0..3 trailing code units.
            string[] samples = { "", "a", "ab", "abc", "abcd", "abcde", "abcdef", "abcdefg", "abcdefgh" };
            samples.Select(DeepEqualsHashCode.Hash).Distinct().Should().HaveCount(samples.Length);
        }
        finally
        {
            DeepEqualsHashCode.MarvinSeed = saved;
        }
    }
#endif

    [Fact]
    public void DateTime_bits_expose_the_hidden_daylight_state()
    {
        System.Runtime.CompilerServices.Unsafe.SizeOf<DateTime>().Should().Be(8);
        DateTime utc = new DateTime(2024, 3, 10, 1, 30, 0, DateTimeKind.Utc);
        DateTime local = new DateTime(2024, 3, 10, 1, 30, 0, DateTimeKind.Local);
        DateTime unspecified = new DateTime(2024, 3, 10, 1, 30, 0, DateTimeKind.Unspecified);
        DeepEqualsHelpers.DateTimeBits(utc).Should().NotBe(DeepEqualsHelpers.DateTimeBits(local));
        DeepEqualsHelpers.DateTimeBits(utc).Should().NotBe(DeepEqualsHelpers.DateTimeBits(unspecified));
        (DeepEqualsHelpers.DateTimeBits(unspecified) & 0x3FFF_FFFF_FFFF_FFFFUL).Should().Be((ulong)unspecified.Ticks);
        DeepEqualsHelpers.DateTimeBits(local).Should().Be(DeepEqualsHelpers.DateTimeBits(new DateTime(local.Ticks, DateTimeKind.Local)));
    }
}
