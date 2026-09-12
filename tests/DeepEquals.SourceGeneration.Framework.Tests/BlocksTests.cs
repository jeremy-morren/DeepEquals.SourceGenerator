// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

// netcoreapp3.1 resolves the netstandard2.1 asset, which has no block helpers on purpose.
#if !NETCOREAPP3_1
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO.Hashing;
using System.Linq;
using System.Runtime.InteropServices;
using FluentAssertions;
using Xunit;

namespace DeepEquals.SourceGeneration.Framework.Tests;

public sealed class BlocksTests : IDisposable
{
    private readonly long _savedSeed = DeepEqualsBlocks.Seed;

    public void Dispose() => DeepEqualsBlocks.Seed = _savedSeed;

    public static readonly int[] Lengths = [0, 1, 3, 16, 17, 128, 129, 240, 241, 4096];

    [Fact]
    public void HashBytes_is_seeded_XxHash3_folded_to_32_bits()
    {
        var random = new Random(99);
        foreach (var seed in new[] { 0L, 7L, unchecked((long)0x9E3779B97F4A7C15UL) })
        {
            DeepEqualsBlocks.Seed = seed;
            foreach (var length in Lengths)
            {
                var bytes = new byte[length];
                random.NextBytes(bytes);
                var full = XxHash3.HashToUInt64(bytes, seed);
                var folded = unchecked((int)(full ^ (full >> 32)));
                DeepEqualsBlocks.HashBytes(bytes).Should().Be(folded == 0 ? 1 : folded, $"length {length}");
            }
        }

        var sample = Enumerable.Range(0, 100).Select(i => (byte)i).ToArray();
        DeepEqualsBlocks.Seed = 1;
        var one = DeepEqualsBlocks.HashBytes(sample);
        DeepEqualsBlocks.Seed = 2;
        DeepEqualsBlocks.HashBytes(sample).Should().NotBe(one, "the seed takes part");
    }

    /// <summary>An IReadOnlyList that is neither an array nor a List, so every copy path is taken.</summary>
    private sealed class Wrapper<T>(T[] items) : IReadOnlyList<T>, IList<T>
    {
        public T this[int index] { get => items[index]; set => throw new NotSupportedException(); }
        public int Count => items.Length;
        public bool IsReadOnly => true;
        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)items).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public void CopyTo(T[] array, int arrayIndex) => items.CopyTo(array, arrayIndex);
        public int IndexOf(T item) => throw new NotSupportedException();
        public void Insert(int index, T item) => throw new NotSupportedException();
        public void RemoveAt(int index) => throw new NotSupportedException();
        public void Add(T item) => throw new NotSupportedException();
        public void Clear() => throw new NotSupportedException();
        public bool Contains(T item) => throw new NotSupportedException();
        public bool Remove(T item) => throw new NotSupportedException();
    }

    private static void AssertAllShapesAgree<T>(T[] items) where T : unmanaged
    {
        var expected = DeepEqualsBlocks.HashBlock<T>(items);
        DeepEqualsBlocks.HashBytes(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(items))).Should().Be(expected);

        var list = new List<T>(items);
        var wrapper = new Wrapper<T>(items);
        IEnumerable<T> lazy = items.Select(x => x);
        foreach (var (name, hash) in new (string, int)[]
                 {
                     ("array as read-only list", DeepEqualsBlocks.HashReadOnlyList<T>(items)),
                     ("List as read-only list", DeepEqualsBlocks.HashReadOnlyList<T>(list)),
                     ("wrapper as read-only list", DeepEqualsBlocks.HashReadOnlyList<T>(wrapper)),
                     ("List as list", DeepEqualsBlocks.HashList<T>(list)),
                     ("wrapper as list", DeepEqualsBlocks.HashList<T>(wrapper)),
                     ("array as enumerable", DeepEqualsBlocks.HashEnumerable<T>(items)),
                     ("wrapper as enumerable", DeepEqualsBlocks.HashEnumerable<T>(wrapper)),
                     ("lazy enumerable", DeepEqualsBlocks.HashEnumerable(lazy)),
                 })
            hash.Should().Be(expected, $"{name} of {items.Length} {typeof(T).Name}");
    }

    [Fact]
    public void HashBlock_HashList_and_HashEnumerable_agree()
    {
        DeepEqualsBlocks.Seed = 42;
        var random = new Random(3);
        foreach (var length in Lengths)
        {
            AssertAllShapesAgree(Enumerable.Range(0, length).Select(_ => random.NextDouble()).ToArray());
            AssertAllShapesAgree(Enumerable.Range(0, length).Select(_ => Guid.NewGuid()).ToArray());
            AssertAllShapesAgree(Enumerable.Range(0, length).Select(i => i / 7m).ToArray());
            AssertAllShapesAgree(Enumerable.Range(0, length).Select(i => (byte)i).ToArray());
        }
    }

    /// <summary>A sequence that advertises a count it does not deliver.</summary>
    private sealed class Liar(int advertised, int[] actual) : IReadOnlyCollection<int>
    {
        public int Count => advertised;
        public IEnumerator<int> GetEnumerator() => ((IEnumerable<int>)actual).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Fact]
    public void HashEnumerable_hashes_what_it_enumerates_whatever_the_count_says()
    {
        var actual = Enumerable.Range(0, 100).ToArray();
        var expected = DeepEqualsBlocks.HashBlock<int>(actual);
        DeepEqualsBlocks.HashEnumerable(new Liar(3, actual)).Should().Be(expected, "the count only sizes the first buffer, which grows");
        DeepEqualsBlocks.HashEnumerable(new Liar(1000, actual)).Should().Be(expected, "extra capacity is not hashed");
    }

    [Fact]
    public void HashEnumerable_returns_its_rented_buffer()
    {
        using var pools = new PoolScope();
        var growing = Enumerable.Range(0, 100).Select(i => i);   // no count, so the 16-slot buffer grows
        DeepEqualsBlocks.HashEnumerable(growing);
        pools.Ints.Outstanding.Should().Be(0);
        pools.Ints.Rents.Should().BeGreaterThan(1, "the buffer grew");

        IEnumerable<int> Throwing()
        {
            yield return 1;
            throw new InvalidOperationException("boom");
        }

        var act = () => DeepEqualsBlocks.HashEnumerable(Throwing());
        act.Should().Throw<InvalidOperationException>();
        pools.Ints.Outstanding.Should().Be(0, "a throwing enumerator still returns the buffer");

        DeepEqualsBlocks.HashList(new Wrapper<int>(Enumerable.Range(0, 50).ToArray()));
        DeepEqualsBlocks.HashReadOnlyList(new Wrapper<int>(Enumerable.Range(0, 500).ToArray()));
        pools.Ints.Outstanding.Should().Be(0);
    }

    [Fact]
    public void BlockEquals_is_bitwise()
    {
        DeepEqualsBlocks.BlockEquals<double>(new[] { 1.0, 2.0 }, new[] { 1.0, 2.0 }).Should().BeTrue();
        DeepEqualsBlocks.BlockEquals<double>(new[] { 0.0 }, new[] { -0.0 }).Should().BeFalse("the sign of zero is a bit");
        DeepEqualsBlocks.BlockEquals<double>(new[] { double.NaN }, new[] { double.NaN }).Should().BeTrue("the same NaN payload is the same bits");
        var otherNaN = BitConverter.ToDouble(BitConverter.GetBytes(0x7FF8000000000001L), 0);
        DeepEqualsBlocks.BlockEquals<double>(new[] { double.NaN }, new[] { otherNaN }).Should().BeFalse("NaN payloads differ");
        // Through locals: Roslyn folds a decimal constant in an array initializer converted straight to a span, and
        // new[] { 1.50m } would reach the span as 1.5.
        decimal oneAndHalf = 1.5m, oneAndHalfScaled = 1.50m;
        DeepEqualsBlocks.BlockEquals<decimal>(new[] { oneAndHalf }, new[] { oneAndHalfScaled }).Should().BeFalse("the scale is part of the value");
        var utc = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        DeepEqualsBlocks.BlockEquals<DateTime>(new[] { utc }, new[] { new DateTime(utc.Ticks, DateTimeKind.Local) }).Should().BeFalse("the kind is part of the value");
        DeepEqualsBlocks.BlockEquals<int>(new[] { 1, 2 }, new[] { 1 }).Should().BeFalse("lengths differ");
        DeepEqualsBlocks.BlockEquals<int>(Array.Empty<int>(), Array.Empty<int>()).Should().BeTrue();
    }

    [Fact]
    public void Empty_block_hashes_nonzero()
    {
        foreach (var seed in new[] { 0L, 1L, -1L, long.MaxValue })
        {
            DeepEqualsBlocks.Seed = seed;
            DeepEqualsBlocks.HashBlock<int>(Array.Empty<int>()).Should().NotBe(0);
            DeepEqualsBlocks.HashEnumerable(Enumerable.Empty<int>()).Should().NotBe(0);
        }
    }

#pragma warning disable CS0649 // layout probes: the fields exist only to be measured
    private struct Padded
    {
        public int A;
        public long B;
    }

    private struct Tight
    {
        public int A;
        public int B;
        public long C;
    }
#pragma warning restore CS0649

    [Fact]
    public void HasSize_detects_padding()
    {
        DeepEqualsBlocks.HasSize<Tight>(16).Should().BeTrue();
        DeepEqualsBlocks.HasSize<Padded>(12).Should().BeFalse("four bytes of padding sit between the int and the long");
        DeepEqualsBlocks.HasSize<decimal>(16).Should().BeTrue();
        DeepEqualsBlocks.HasSize<Guid>(16).Should().BeTrue();
    }
}
#endif
