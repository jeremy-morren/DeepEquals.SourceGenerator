// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace DeepEquals.SourceGeneration.Framework.Tests;

public sealed class UnorderedTests
{
    // ----- stateless ops over ints and strings ------------------------------------------------------------------------

    private struct IntOps : IDeepEqualsStatelessElementOps<int>
    {
        public bool Equals(int x, int y) => x == y;
        public int GetHashCode(int x) => x;
    }

    /// <summary>Every element hashes alike, so every comparison goes through the exact matching path.</summary>
    private struct ConstantHashIntOps : IDeepEqualsStatelessElementOps<int>
    {
        public bool Equals(int x, int y) => x == y;
        public int GetHashCode(int x) => 42;
    }

    private struct StringIntPairOps : IDeepEqualsStatelessElementOps<KeyValuePair<string, int>>
    {
        public bool Equals(KeyValuePair<string, int> x, KeyValuePair<string, int> y) => string.Equals(x.Key, y.Key, StringComparison.Ordinal) && x.Value == y.Value;
        public int GetHashCode(KeyValuePair<string, int> x) => DeepEqualsHashCode.Combine(DeepEqualsHashCode.Hash(x.Key), x.Value);
    }

    private static bool Sets(IEnumerable<int> x, IEnumerable<int> y, int count, int cap = 64)
        => DeepEqualsUnordered.SetEquals<int, IntOps>(x, y, count, cap);

    [Fact]
    public void Equal_sets_in_different_orders_are_equal()
    {
        HashSet<int> a = new HashSet<int> { 1, 2, 3, 4, 5 };
        SortedSet<int> b = new SortedSet<int> { 5, 4, 3, 2, 1 };
        Sets(a, b, 5).Should().BeTrue();
        Sets(b, a, 5).Should().BeTrue();
        Sets(a, new HashSet<int> { 1, 2, 3, 4, 6 }, 5).Should().BeFalse();
    }

    [Fact]
    public void Empty_sets_are_equal_without_enumeration()
    {
        Sets(new ThrowingEnumerable<int>(), new ThrowingEnumerable<int>(), 0).Should().BeTrue();
    }

    [Fact]
    public void Multiset_semantics_apply_to_duplicates_under_a_finer_collection_comparer()
    {
        // The library's element equality is coarser than the collection's own: two "distinct" entries are deep-equal.
        int[] x = { 1, 1, 2 };
        int[] y = { 1, 2, 2 };
        Sets(x, y, 3).Should().BeFalse("multiplicities differ");
        Sets(x, new[] { 2, 1, 1 }, 3).Should().BeTrue();
    }

    [Fact]
    public void Hash_sum_short_circuit_and_run_lengths_reject_unequal_multisets()
    {
        DeepEqualsUnordered.SetEquals<int, ConstantHashIntOps>(new[] { 1, 2, 3 }, new[] { 1, 2, 4 }, 3, 64).Should().BeFalse();
        DeepEqualsUnordered.SetEquals<int, ConstantHashIntOps>(new[] { 1, 2, 3 }, new[] { 3, 1, 2 }, 3, 64).Should().BeTrue();
    }

    [Fact]
    public void Collision_run_over_the_cap_throws_and_under_the_cap_succeeds()
    {
        int[] x = Enumerable.Range(0, 65).ToArray();
        int[] y = Enumerable.Range(0, 65).Reverse().ToArray();
        Action over = () => DeepEqualsUnordered.SetEquals<int, ConstantHashIntOps>(x, y, 65, 64);
        DeepEqualsComplexityException ex = over.Should().Throw<DeepEqualsComplexityException>().Which;
        ex.RunLength.Should().Be(65);
        ex.Cap.Should().Be(64);
        ex.CollectionType.Should().Be(typeof(int[]));
        DeepEqualsUnordered.SetEquals<int, ConstantHashIntOps>(x, y, 65, 128).Should().BeTrue();
        DeepEqualsUnordered.SetEquals<int, ConstantHashIntOps>(x.Take(64).ToArray(), y.Skip(1).ToArray(), 64, 64).Should().BeTrue();
    }

    [Fact]
    public void Count_lies_are_detected_on_the_enumerating_path()
    {
        Action tooMany = () => Sets(new[] { 1, 2, 3 }, new[] { 1, 2, 3 }, 2);
        tooMany.Should().Throw<InvalidOperationException>();
        Action tooFew = () => Sets(new[] { 1, 2 }, new[] { 1, 2 }, 3);
        tooFew.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Dictionaries_compare_as_multisets_of_pairs_regardless_of_their_own_comparer()
    {
        Dictionary<string, int> a = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase) { ["A"] = 1, ["b"] = 2 };
        Dictionary<string, int> b = new Dictionary<string, int>(StringComparer.Ordinal) { ["b"] = 2, ["A"] = 1 };
        Dictionary<string, int> c = new Dictionary<string, int>(StringComparer.Ordinal) { ["a"] = 1, ["b"] = 2 };
        DeepEqualsUnordered.DictionaryEquals<string, int, StringIntPairOps>(a, b, 2, 64).Should().BeTrue();
        DeepEqualsUnordered.DictionaryEquals<string, int, StringIntPairOps>(a, c, 2, 64).Should().BeFalse("keys are ordinal here");
        DeepEqualsUnordered.DictionaryEquals<string, int, StringIntPairOps>(a, a.ToList(), 2, 64).Should().BeTrue("a non-Dictionary implementation enumerates through the interface");
    }

    [Fact]
    public void Every_rented_array_is_returned_and_reference_arrays_are_cleared()
    {
        using PoolScope pools = new PoolScope();
        FakeArrayPool<string> strings = new FakeArrayPool<string>(() => "garbage");
        var saved = DeepEqualsPools<string>.Shared;
        DeepEqualsPools<string>.Shared = strings;
        try
        {
            DeepEqualsUnordered.SetEquals<string, StringOps>(new[] { "x", "y" }, new[] { "y", "x" }, 2, 64).Should().BeTrue();
            strings.Outstanding.Should().Be(0);
            strings.Returned.Should().OnlyContain(a => Array.TrueForAll(a, s => s == null));
            pools.Longs.Outstanding.Should().Be(0);

            // Exception from an element comparer in the exact-matching path still returns everything.
            Action throwing = () => DeepEqualsUnordered.SetEquals<string, ThrowingStringOps>(new[] { "x", "y" }, new[] { "y", "x" }, 2, 64);
            throwing.Should().Throw<InvalidOperationException>().WithMessage("boom");
            strings.Outstanding.Should().Be(0);
            pools.Longs.Outstanding.Should().Be(0);
            pools.ULongs.Outstanding.Should().Be(0);
            pools.Ints.Outstanding.Should().Be(0);

            // A failing rent returns the earlier rentals exactly once.
            pools.Longs.FailOnRent = pools.Longs.Rents + 1;
            Action failing = () => DeepEqualsUnordered.SetEquals<string, StringOps>(new[] { "x" }, new[] { "x" }, 1, 64);
            failing.Should().Throw<OutOfMemoryException>();
            strings.Outstanding.Should().Be(0);
        }
        finally
        {
            DeepEqualsPools<string>.Shared = saved;
        }
    }

    private struct StringOps : IDeepEqualsStatelessElementOps<string>
    {
        public bool Equals(string x, string y) => string.Equals(x, y, StringComparison.Ordinal);
        public int GetHashCode(string x) => DeepEqualsHashCode.Hash(x);
    }

    private struct ThrowingStringOps : IDeepEqualsStatelessElementOps<string>
    {
        public bool Equals(string x, string y) => throw new InvalidOperationException("boom");
        public int GetHashCode(string x) => 7;
    }

    // ----- matching oracle --------------------------------------------------------------------------------------------

    /// <summary>Elements carry an index into a compatibility matrix supplied by the test; every element hashes alike.</summary>
    private sealed class Cell
    {
        public Cell(int index, bool[,] matrix, bool left)
        {
            Index = index;
            Matrix = matrix;
            Left = left;
        }

        public int Index { get; }
        public bool[,] Matrix { get; }
        public bool Left { get; }
    }

    private struct MatrixOps : IDeepEqualsStatelessElementOps<Cell>
    {
        public bool Equals(Cell x, Cell y)
        {
            Cell left = x.Left ? x : y;
            Cell right = x.Left ? y : x;
            return left.Matrix[left.Index, right.Index];
        }

        public int GetHashCode(Cell x) => 0;
    }

    private static bool BruteForcePerfectMatching(bool[,] matrix, int k)
    {
        int[] perm = Enumerable.Range(0, k).ToArray();
        return Permutations(perm, 0).Any(p => Enumerable.Range(0, k).All(i => matrix[i, p[i]]));
    }

    private static IEnumerable<int[]> Permutations(int[] items, int start)
    {
        if (start == items.Length)
        {
            yield return (int[])items.Clone();
            yield break;
        }

        for (int i = start; i < items.Length; i++)
        {
            (items[start], items[i]) = (items[i], items[start]);
            foreach (int[] p in Permutations(items, start + 1)) yield return p;
            (items[start], items[i]) = (items[i], items[start]);
        }
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Exact_matching_agrees_with_brute_force_on_every_matrix_and_permutation(int k)
    {
        int cells = k * k;
        for (int mask = 0; mask < (1 << cells); mask++)
        {
            bool[,] matrix = new bool[k, k];
            for (int c = 0; c < cells; c++) matrix[c / k, c % k] = (mask & (1 << c)) != 0;
            bool expected = BruteForcePerfectMatching(matrix, k);

            foreach (int[] rowOrder in Permutations(Enumerable.Range(0, k).ToArray(), 0).Take(6))
            {
                Cell[] left = rowOrder.Select(i => new Cell(i, matrix, left: true)).ToArray();
                Cell[] right = Enumerable.Range(0, k).Reverse().Select(j => new Cell(j, matrix, left: false)).ToArray();
                DeepEqualsUnordered.SetEquals<Cell, MatrixOps>(left, right, k, 64).Should().Be(expected, $"k={k} mask={mask}");
                DeepEqualsUnordered.SetEquals<Cell, MatrixOps>(right, left, k, 64).Should().Be(expected, $"k={k} mask={mask} swapped");
            }
        }
    }

    [Fact]
    public void Greedy_first_fit_would_fail_where_exact_matching_succeeds()
    {
        // left0 matches right0 and right1; left1 matches right0 only. Greedy pairs left0 with right0 and strands left1.
        bool[,] matrix = { { true, true }, { true, false } };
        Cell[] left = { new Cell(0, matrix, true), new Cell(1, matrix, true) };
        Cell[] right = { new Cell(0, matrix, false), new Cell(1, matrix, false) };
        DeepEqualsUnordered.SetEquals<Cell, MatrixOps>(left, right, 2, 64).Should().BeTrue();
    }

    // ----- stateful: a hand-written cyclic node core -------------------------------------------------------------------

    private sealed class Node
    {
        public int Value;
        public Node? Next;
        public Node(int value) => Value = value;
    }

    private const int NodeKind = 1;

    private static bool EqualsNode(Node? x, Node? y, ref DeepEqualsState state)
    {
        while (true)
        {
            if (ReferenceEquals(x, y)) return true;
            if (x is null || y is null) return false;
            if (!state.TryEnter(NodeKind, x, y)) return true;
            if (x.Value != y.Value) return false;
            x = x.Next;
            y = y.Next;
        }
    }

    private static int HashNode(Node? o) => o is null ? 0 : DeepEqualsHashCode.Combine(o.Value, o.Next is null ? 0 : o.Next.Value);

    private struct NodeOps : IDeepEqualsElementOps<Node>
    {
        public bool Equals(Node x, Node y, ref DeepEqualsState state) => EqualsNode(x, y, ref state);
        public int GetHashCode(Node x) => HashNode(x);
    }

    private static Node Cycle(params int[] values)
    {
        Node[] nodes = values.Select(v => new Node(v)).ToArray();
        for (int i = 0; i < nodes.Length; i++) nodes[i].Next = nodes[(i + 1) % nodes.Length];
        return nodes[0];
    }

    [Fact]
    public void Stateful_sets_of_cyclic_elements_compare_equal_and_trials_roll_back()
    {
        HashSet<Node> a = new HashSet<Node> { Cycle(1, 2), Cycle(3), Cycle(1, 2, 1, 2) };
        HashSet<Node> b = new HashSet<Node> { Cycle(3), Cycle(1, 2), Cycle(1, 2) };
        DeepEqualsState state = new DeepEqualsState(1000);
        try
        {
            DeepEqualsUnordered.SetEquals<Node, NodeOps>(a, b, 3, 64, ref state).Should().BeTrue("rolled and unrolled cycles are equal, whatever the enumeration order");
            state.Count.Should().BeGreaterThan(0, "committed pairs are retained");
        }
        finally
        {
            state.Dispose();
        }

        HashSet<Node> c = new HashSet<Node> { Cycle(3), Cycle(1, 2), Cycle(2, 1) };
        state = new DeepEqualsState(1000);
        try
        {
            // Cycle(1,2) and Cycle(2,1) hash alike here, forcing the exact path with rollback between trials.
            DeepEqualsUnordered.SetEquals<Node, NodeOps>(a, c, 3, 64, ref state).Should().BeFalse();
        }
        finally
        {
            state.Dispose();
        }
    }

    [Fact]
    public void Commit_that_changes_its_answer_fails_closed()
    {
        DeepEqualsState state = new DeepEqualsState(1000);
        try
        {
            FlipFlopOps.Calls = 0;
            Node[] x = { new Node(1), new Node(1) };
            Node[] y = { new Node(1), new Node(1) };
            DeepEqualsUnordered.SetEquals<Node, FlipFlopOps>(x, y, 2, 64, ref state).Should().BeFalse();
        }
        finally
        {
            state.Dispose();
        }
    }

    /// <summary>Answers true during the four trials of a 2x2 run and false afterwards, simulating an unstable comparer.</summary>
    private struct FlipFlopOps : IDeepEqualsElementOps<Node>
    {
        public static int Calls;
        public bool Equals(Node x, Node y, ref DeepEqualsState state) => ++Calls <= 4;
        public int GetHashCode(Node x) => 0;
    }

    private sealed class ThrowingEnumerable<T> : IEnumerable<T>
    {
        public IEnumerator<T> GetEnumerator() => throw new InvalidOperationException("must not enumerate");
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
