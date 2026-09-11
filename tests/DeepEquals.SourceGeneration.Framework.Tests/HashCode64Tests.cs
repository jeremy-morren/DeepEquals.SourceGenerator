// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Linq;
using FluentAssertions;
using Xunit;

// ReSharper disable ConvertToConstant.Local

namespace DeepEquals.SourceGeneration.Framework.Tests;

public sealed class HashCode64Tests : IDisposable
{
    private readonly ulong _savedSeed = DeepEqualsHashCode64.Seed;

    public void Dispose() => DeepEqualsHashCode64.Seed = _savedSeed;

    /// <summary>An independent, deliberately naive xxHash64 over 64-bit words, written from the algorithm description.</summary>
    private static ulong Reference(ulong seed, ulong[] inputs)
    {
        const ulong p1 = 11400714785074694791UL, p2 = 14029467366897019727UL, p3 = 1609587929392839161UL, p4 = 9650029242287828579UL, p5 = 2870177450012600261UL;
        static ulong Rol(ulong v, int n) => (v << n) | (v >> (64 - n));
        static ulong Round(ulong acc, ulong input) => Rol(acc + input * p2, 31) * p1;
        static ulong Merge(ulong h, ulong v) => (h ^ Round(0, v)) * p1 + p4;
        unchecked
        {
            ulong hash;
            var i = 0;
            if (inputs.Length >= 4)
            {
                ulong v1 = seed + p1 + p2, v2 = seed + p2, v3 = seed, v4 = seed - p1;
                for (; i + 4 <= inputs.Length; i += 4)
                {
                    v1 = Round(v1, inputs[i]);
                    v2 = Round(v2, inputs[i + 1]);
                    v3 = Round(v3, inputs[i + 2]);
                    v4 = Round(v4, inputs[i + 3]);
                }

                hash = Rol(v1, 1) + Rol(v2, 7) + Rol(v3, 12) + Rol(v4, 18);
                hash = Merge(hash, v1);
                hash = Merge(hash, v2);
                hash = Merge(hash, v3);
                hash = Merge(hash, v4);
            }
            else hash = seed + p5;

            hash += (ulong)inputs.Length * 8;
            for (; i < inputs.Length; i++) hash = Rol(hash ^ Round(0, inputs[i]), 27) * p1 + p4;

            hash ^= hash >> 33;
            hash *= p2;
            hash ^= hash >> 29;
            hash *= p3;
            hash ^= hash >> 32;
            return hash;
        }
    }

    private static ulong Combine(ulong[] inputs)
    {
        var result = typeof(DeepEqualsHashCode64)
            .GetMethods()
            .Single(m => m.Name == "Combine" && m.GetParameters().Length == inputs.Length)
            .Invoke(null, inputs.Cast<object>().ToArray())!;
        return (ulong)result;
    }

    private static ulong Streaming(ulong[] inputs)
    {
        DeepEqualsHashCode64.Streaming streaming = default;
        foreach (var input in inputs) streaming.Add(input);

        return streaming.ToHashCode();
    }

    private static ulong[] Words(Random random, int count) =>
        Enumerable.Range(0, count).Select(_ => ((ulong)(uint)random.Next() << 32) | (uint)random.Next()).ToArray();

    [Theory]
    [InlineData(0UL)]
    [InlineData(0x9E3779B97F4A7C15UL)]
    [InlineData(ulong.MaxValue)]
    public void Combine64_matches_reference_for_every_arity(ulong seed)
    {
        DeepEqualsHashCode64.Seed = seed;
        var random = new Random(4321);
        for (var arity = 1; arity <= 32; arity++)
        {
            for (var trial = 0; trial < 20; trial++)
            {
                var inputs = Words(random, arity);
                Combine(inputs).Should().Be(Reference(seed, inputs), $"arity {arity} must be the xxHash64 stream");
                Streaming(inputs).Should().Be(Reference(seed, inputs), $"streaming of {arity} inputs must be the same stream");
            }
        }
    }

    [Fact]
    public void Empty_stream_with_seed_zero_is_the_published_xxHash64_vector()
    {
        DeepEqualsHashCode64.Seed = 0;
        Streaming([]).Should().Be(0xEF46DB3751D8E999UL);
    }

    [Fact]
    public void Empty_stream_never_returns_zero()
    {
        DeepEqualsHashCode64.Seed = 0;
        Streaming([]).Should().NotBe(0UL);
        DeepEqualsHashCode64.FoldNonZero(Streaming([])).Should().NotBe(0);
        DeepEqualsHashCode64.Seed = 0xDEADBEEFCAFEF00DUL;
        Streaming([]).Should().NotBe(0UL);
        DeepEqualsHashCode64.FoldNonZero(Streaming([])).Should().NotBe(0);
    }

    [Fact]
    public void Fold_of_a_nonzero_value_can_be_zero_and_FoldNonZero_maps_it_to_one()
    {
        DeepEqualsHashCode64.Fold(0x0000_0001_0000_0001UL).Should().Be(0, "equal halves cancel");
        DeepEqualsHashCode64.FoldNonZero(0x0000_0001_0000_0001UL).Should().Be(1);
        DeepEqualsHashCode64.FoldNonZero(0UL).Should().Be(1);
        DeepEqualsHashCode64.FoldNonZero(0x0000_0002_0000_0001UL).Should().Be(3);
    }

    [Fact]
    public void Streaming64_lengths_around_lane_boundaries_match_reference()
    {
        DeepEqualsHashCode64.Seed = 77;
        for (var length = 0; length <= 40; length++)
        {
            var inputs = Enumerable.Range(1, length).Select(i => (ulong)i * 7919UL * 0x1_0000_0001UL).ToArray();
            var expected = length == 0 ? Streaming(inputs) : Reference(77, inputs);
            Streaming(inputs).Should().Be(expected);
        }
    }

#if !NET472
    private struct IdentityOps : IDeepEqualsHashOps64<ulong>
    {
        public ulong GetHashCode64(ulong x) => x;
    }

    [Fact]
    public void HashSpan64_matches_streaming_and_combine()
    {
        DeepEqualsHashCode64.Seed = 99;
        for (var length = 0; length <= 40; length++)
        {
            var inputs = Enumerable.Range(1, length).Select(i => (ulong)i * 104729UL).ToArray();
            var viaSpan = DeepEqualsHashCode64.HashSpan<ulong, IdentityOps>(inputs);
            viaSpan.Should().Be(Streaming(inputs));
            if (length is >= 1 and <= 32) viaSpan.Should().Be(Combine(inputs));
        }
    }
#endif

    [Fact]
    public void Fold_uses_both_halves()
    {
        DeepEqualsHashCode64.Fold(0x0000_0000_1234_5678UL).Should().NotBe(DeepEqualsHashCode64.Fold(0x0000_0001_1234_5678UL));
        DeepEqualsHashCode64.Fold(0x1234_5678_0000_0000UL).Should().Be(0x1234_5678);
        DeepEqualsHashCode64.Fold(0x0000_0000_1234_5678UL).Should().Be(0x1234_5678);
    }

    [Fact]
    public void Pack_casts_through_uint()
    {
        DeepEqualsHashCode64.Pack(1, -1).Should().Be(0x0000_0001_FFFF_FFFFUL, "a negative low word must not sign-extend over the high one");
        DeepEqualsHashCode64.Pack(-1, 1).Should().Be(0xFFFF_FFFF_0000_0001UL);
        DeepEqualsHashCode64.Pack(7, 9).Should().NotBe(DeepEqualsHashCode64.Pack(9, 7));
        DeepEqualsHashCode64.Narrow(-1).Should().Be(0x0000_0000_FFFF_FFFFUL);
        DeepEqualsHashCode64.Narrow(5).Should().Be(5UL);
    }

    [Fact]
    public void Decimal_and_guid_hashes_use_both_words()
    {
        DeepEqualsHashCode64.Seed = 5;
        DeepEqualsHashCode64.Hash(1.5m).Should().NotBe(DeepEqualsHashCode64.Hash(1.50m), "the scale is part of the value");
        DeepEqualsHashCode64.Hash(1.5m).Should().Be(DeepEqualsHashCode64.Combine(DeepEqualsHelpers.DecimalLo64(1.5m), DeepEqualsHelpers.DecimalHi64(1.5m)));
        DeepEqualsHashCode64.Hash(decimal.Negate(0m)).Should().NotBe(DeepEqualsHashCode64.Hash(0m));

        var bytes = new byte[16];
        bytes[8] = 1;   // a byte .NET Framework's Guid.GetHashCode ignores, and one .NET Core folds
        var g1 = new Guid(bytes);
        bytes[8] = 0;
        bytes[12] = 1;
        var g2 = new Guid(bytes);
        DeepEqualsHashCode64.Hash(g1).Should().NotBe(DeepEqualsHashCode64.Hash(g2));
        DeepEqualsHashCode64.Hash(g1).Should().Be(DeepEqualsHashCode64.Combine(DeepEqualsHelpers.GuidLo64(g1), DeepEqualsHelpers.GuidHi64(g1)));
    }

    [Fact]
    public void Wide_leaves_are_one_word()
    {
        DeepEqualsHashCode64.Seed = 5;
        var a = 0x0000_0001_0000_0001L;
        var b = 0x0000_0002_0000_0002L;
        DeepEqualsHashCode64.Hash(a).Should().NotBe(DeepEqualsHashCode64.Hash(b));
        DeepEqualsHashCode64.Hash(a).Should().Be(DeepEqualsHashCode64.Combine(unchecked((ulong)a)));
        DeepEqualsHashCode64.Hash(unchecked((ulong)a)).Should().Be(DeepEqualsHashCode64.Hash(a));
        DeepEqualsHashCode64.Hash(1.0).Should().Be(DeepEqualsHashCode64.Combine(DeepEqualsHelpers.DoubleBits(1.0)));
        DeepEqualsHashCode64.Hash(-0.0).Should().NotBe(DeepEqualsHashCode64.Hash(0.0));
        DeepEqualsHashCode64.Hash("abc").Should().Be(DeepEqualsHashCode64.Narrow(DeepEqualsHashCode.Hash("abc")));
        DeepEqualsHashCode64.Hash((string?)null).Should().Be(0UL);
    }

    [Fact]
    public void Seed64_is_independent_of_the_32_bit_seed()
    {
        var saved32 = DeepEqualsHashCode.Seed;
        try
        {
            DeepEqualsHashCode64.Seed = 1;
            var one = DeepEqualsHashCode64.Combine(42UL);
            DeepEqualsHashCode.Seed = 2;
            DeepEqualsHashCode64.Combine(42UL).Should().Be(one, "the 32-bit seed does not take part");
            DeepEqualsHashCode64.Seed = 2;
            DeepEqualsHashCode64.Combine(42UL).Should().NotBe(one, "the 64-bit seed does");
        }
        finally
        {
            DeepEqualsHashCode.Seed = saved32;
        }
    }
}
