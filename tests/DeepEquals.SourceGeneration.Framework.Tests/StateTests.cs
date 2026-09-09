using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace DeepEquals.SourceGeneration.Framework.Tests;

public sealed class StateTests
{
    private static object[] Objects(int n)
    {
        object[] result = new object[n];
        for (int i = 0; i < n; i++)
        {
            result[i] = new object();
        }

        return result;
    }

    [Fact]
    public void Novel_triples_enter_and_repeats_hit()
    {
        object a = new object(), b = new object();
        DeepEqualsState state = new DeepEqualsState(100);
        try
        {
            state.TryEnter(1, a, b).Should().BeTrue();
            state.TryEnter(1, a, b).Should().BeFalse("the same triple is a memo hit");
            state.TryEnter(2, a, b).Should().BeTrue("a different kind is a different relation");
            state.TryEnter(1, b, a).Should().BeTrue("the key is ordered");
            state.Count.Should().Be(3);
            state.HasSpilled.Should().BeFalse();
        }
        finally
        {
            state.Dispose();
        }
    }

    [Fact]
    public void Spills_at_the_ninth_pair_and_keeps_every_earlier_pair()
    {
        using PoolScope pools = new PoolScope();
        object[] xs = Objects(20), ys = Objects(20);
        DeepEqualsState state = new DeepEqualsState(1000);
        try
        {
            for (int i = 0; i < 8; i++)
            {
                state.TryEnter(1, xs[i], ys[i]).Should().BeTrue();
            }

            state.HasSpilled.Should().BeFalse();
            pools.Pairs.Rents.Should().Be(0);

            state.TryEnter(1, xs[8], ys[8]).Should().BeTrue();
            state.HasSpilled.Should().BeTrue();
            pools.Pairs.Rents.Should().Be(1);
            pools.Ints.Rents.Should().Be(1);

            for (int i = 0; i < 9; i++)
            {
                state.TryEnter(1, xs[i], ys[i]).Should().BeFalse($"pair {i} was retained across the spill");
            }

            state.TryEnter(1, xs[0], ys[1]).Should().BeTrue("a mismatched pairing is novel");
        }
        finally
        {
            state.Dispose();
        }

        pools.Pairs.Outstanding.Should().Be(0);
        pools.Ints.Outstanding.Should().Be(0);
        pools.Pairs.Returned.Should().OnlyContain(array => Array.TrueForAll(array, p => p.X == null && p.Y == null), "reference-bearing arrays are cleared on return");
    }

    [Fact]
    public void Grows_past_the_initial_spill_capacity_and_still_finds_every_pair()
    {
        using PoolScope pools = new PoolScope();
        const int n = 500;
        object[] xs = Objects(n), ys = Objects(n);
        DeepEqualsState state = new DeepEqualsState(100_000);
        try
        {
            for (int i = 0; i < n; i++)
            {
                state.TryEnter(i % 3, xs[i], ys[i]).Should().BeTrue();
            }

            for (int i = 0; i < n; i++)
            {
                state.TryEnter(i % 3, xs[i], ys[i]).Should().BeFalse();
                state.TryEnter(i % 3 + 5, xs[i], ys[i]).Should().BeTrue();
            }

            state.Count.Should().Be(2 * n);
            pools.Pairs.Rents.Should().BeGreaterThan(2, "capacity doubled several times");
            pools.Pairs.Outstanding.Should().Be(1, "only the current journal is held");
            pools.Ints.Outstanding.Should().Be(1, "only the current index is held");
        }
        finally
        {
            state.Dispose();
        }

        pools.Pairs.Outstanding.Should().Be(0);
        pools.Ints.Outstanding.Should().Be(0);
    }

    [Fact]
    public void Rollback_forgets_only_pairs_after_the_mark_inline_and_spilled()
    {
        using PoolScope pools = new PoolScope();
        object[] xs = Objects(64), ys = Objects(64);
        DeepEqualsState state = new DeepEqualsState(1000);
        try
        {
            for (int i = 0; i < 5; i++) state.TryEnter(1, xs[i], ys[i]);
            int inlineMark = state.Mark();
            for (int i = 5; i < 8; i++) state.TryEnter(1, xs[i], ys[i]);
            state.Rollback(inlineMark);
            state.Count.Should().Be(5);
            for (int i = 5; i < 8; i++) state.TryEnter(1, xs[i], ys[i]).Should().BeTrue("rolled-back inline pairs are novel again");
            for (int i = 0; i < 5; i++) state.TryEnter(1, xs[i], ys[i]).Should().BeFalse();

            // A mark taken inline, a trial that spills and grows, then a rollback to the inline mark.
            state.Rollback(inlineMark);
            for (int i = 5; i < 64; i++) state.TryEnter(1, xs[i], ys[i]).Should().BeTrue();
            state.HasSpilled.Should().BeTrue();
            state.Rollback(inlineMark);
            state.Count.Should().Be(5);
            state.HasSpilled.Should().BeTrue("rollback does not un-spill");
            for (int i = 0; i < 5; i++) state.TryEnter(1, xs[i], ys[i]).Should().BeFalse("pairs before the mark survive");
            for (int i = 5; i < 64; i++) state.TryEnter(1, xs[i], ys[i]).Should().BeTrue("pairs after the mark were forgotten");

            // Rollback across a growth boundary in spilled mode.
            int spilledMark = state.Mark();
            object[] more = Objects(300);
            for (int i = 0; i < 300; i++) state.TryEnter(2, more[i], more[i]).Should().BeTrue();
            state.Rollback(spilledMark);
            state.Count.Should().Be(spilledMark);
            for (int i = 0; i < 64; i++) state.TryEnter(1, xs[i], ys[i]).Should().BeFalse("the table before the trial is intact after growth and rollback");
            for (int i = 0; i < 300; i++) state.TryEnter(2, more[i], more[i]).Should().BeTrue();
        }
        finally
        {
            state.Dispose();
        }

        pools.Pairs.Outstanding.Should().Be(0);
        pools.Ints.Outstanding.Should().Be(0);
    }

    [Fact]
    public void Budget_applies_only_to_novel_insertions()
    {
        object[] xs = Objects(12), ys = Objects(12);
        DeepEqualsState state = new DeepEqualsState(10);
        try
        {
            for (int i = 0; i < 10; i++) state.TryEnter(1, xs[i], ys[i]).Should().BeTrue();
            state.TryEnter(1, xs[3], ys[3]).Should().BeFalse("a memo hit at the boundary succeeds");
            DeepEqualsComplexityException? thrown = null;
            try { state.TryEnter(1, xs[10], ys[10]); } catch (DeepEqualsComplexityException e) { thrown = e; }
            thrown.Should().NotBeNull();
            thrown!.Pairs.Should().Be(10);
            state.TryEnter(1, xs[9], ys[9]).Should().BeFalse("the failed insertion changed nothing");
        }
        finally
        {
            state.Dispose();
        }
    }

    [Fact]
    public void Budget_smaller_than_the_inline_buffer_is_honoured()
    {
        object[] xs = Objects(3), ys = Objects(3);
        DeepEqualsState state = new DeepEqualsState(1);
        state.TryEnter(1, xs[0], ys[0]).Should().BeTrue();
        state.TryEnter(1, xs[0], ys[0]).Should().BeFalse();
        bool threw = false;
        try { state.TryEnter(1, xs[1], ys[1]); } catch (DeepEqualsComplexityException) { threw = true; }
        threw.Should().BeTrue();
        state.Dispose();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData((1 << 29) + 1)]
    [InlineData(int.MaxValue)]
    public void Constructor_rejects_out_of_range_budgets(int budget)
    {
        Action act = () => new DeepEqualsState(budget);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_accepts_the_boundaries()
    {
        new DeepEqualsState(1).Dispose();
        new DeepEqualsState(1 << 29).Dispose();
    }

    [Fact]
    public void Failed_growth_leaves_the_old_state_intact_and_disposable()
    {
        using PoolScope pools = new PoolScope();
        object[] xs = Objects(64), ys = Objects(64);
        DeepEqualsState state = new DeepEqualsState(1000);
        try
        {
            for (int i = 0; i < 32; i++) state.TryEnter(1, xs[i], ys[i]).Should().BeTrue();   // exactly the initial spill capacity
            pools.Pairs.FailOnRent = pools.Pairs.Rents + 1;
            bool threw = false;
            try { state.TryEnter(1, xs[32], ys[32]); } catch (OutOfMemoryException) { threw = true; }
            threw.Should().BeTrue();
            pools.Pairs.FailOnRent = 0;
            for (int i = 0; i < 32; i++) state.TryEnter(1, xs[i], ys[i]).Should().BeFalse("the old table survived the failed growth");
            state.TryEnter(1, xs[32], ys[32]).Should().BeTrue("growth succeeds once the pool recovers");
        }
        finally
        {
            state.Dispose();
        }

        pools.Pairs.Outstanding.Should().Be(0);
        pools.Ints.Outstanding.Should().Be(0);
    }

    [Fact]
    public void Failed_index_rent_during_spill_returns_the_pair_array()
    {
        using PoolScope pools = new PoolScope();
        object[] xs = Objects(10), ys = Objects(10);
        DeepEqualsState state = new DeepEqualsState(1000);
        try
        {
            for (int i = 0; i < 8; i++) state.TryEnter(1, xs[i], ys[i]);
            pools.Ints.FailOnRent = 1;
            bool threw = false;
            try { state.TryEnter(1, xs[8], ys[8]); } catch (OutOfMemoryException) { threw = true; }
            threw.Should().BeTrue();
            pools.Pairs.Outstanding.Should().Be(0, "the pair array rented before the failing rent was returned exactly once");
            state.HasSpilled.Should().BeFalse();
            pools.Ints.FailOnRent = 0;
            state.TryEnter(1, xs[8], ys[8]).Should().BeTrue();
            for (int i = 0; i < 8; i++) state.TryEnter(1, xs[i], ys[i]).Should().BeFalse();
        }
        finally
        {
            state.Dispose();
        }

        pools.Pairs.Outstanding.Should().Be(0);
        pools.Ints.Outstanding.Should().Be(0);
    }

    [Fact]
    public void Dirty_and_oversized_pool_arrays_do_not_produce_false_hits()
    {
        using PoolScope pools = new PoolScope();
        object[] xs = Objects(200), ys = Objects(200);
        DeepEqualsState state = new DeepEqualsState(1000);
        try
        {
            HashSet<int> seen = new HashSet<int>();
            for (int i = 0; i < 200; i++)
            {
                state.TryEnter(1, xs[i], ys[i]).Should().BeTrue($"pair {i} is novel despite garbage in the rented arrays");
            }

            for (int i = 0; i < 200; i++)
            {
                state.TryEnter(1, xs[i], ys[i]).Should().BeFalse();
            }
        }
        finally
        {
            state.Dispose();
        }
    }
}
