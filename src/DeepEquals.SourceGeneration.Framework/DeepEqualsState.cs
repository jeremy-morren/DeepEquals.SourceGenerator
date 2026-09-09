// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace DeepEquals.SourceGeneration.Framework;

/// <summary>
/// Per-call comparison state: the retained <c>(kind, x, y)</c> triples of one deep comparison. Declare as a local in the
/// entry point, pass by <c>ref</c>, never store. Eight triples live inline; beyond that the state spills into rented arrays
/// that <see cref="Dispose"/> returns.
/// </summary>
public ref struct DeepEqualsState
{
    /// <summary>The largest pair budget the capacity arithmetic supports: 2^29.</summary>
    public const int MaxPairBudget = 1 << 29;

    private const int InlineCapacity = 8;
    private const int InitialSpillCapacity = 32;

#if NET8_0_OR_GREATER
    [InlineArray(InlineCapacity)]
    private struct InlineBuffer
    {
        private ReferencePair _element0;
    }

    private InlineBuffer _inline;
#else
    private ReferencePair _i0;
    private ReferencePair _i1;
    private ReferencePair _i2;
    private ReferencePair _i3;
    private ReferencePair _i4;
    private ReferencePair _i5;
    private ReferencePair _i6;
    private ReferencePair _i7;
#endif

    private int _count;                  // distinct retained triples; doubles as the journal length
    private readonly ArrayPool<ReferencePair> _pairPool;
    private readonly ArrayPool<int> _indexPool;
    private readonly int _pairBudget;
    private ReferencePair[]? _pairs;     // rented; insertion order, so it is the journal
    private int[]? _index;               // rented; slot value = pair index + 1, 0 = empty; only the logical prefix participates
    private int _pairsCapacity;          // logical capacity, <= _pairs.Length and <= _pairBudget
    private int _indexCapacity;          // logical power of two, >= 2 * _pairsCapacity, <= _index.Length

    /// <summary>Creates a state whose retained-triple budget is <paramref name="pairBudget"/>, 1 through 2^29.</summary>
    public DeepEqualsState(int pairBudget)
    {
        if (pairBudget < 1 || pairBudget > MaxPairBudget)
        {
            throw new ArgumentOutOfRangeException(nameof(pairBudget), pairBudget, "The pair budget must be between 1 and 2^29.");
        }

        _pairPool = DeepEqualsPools<ReferencePair>.Shared;
        _indexPool = DeepEqualsPools<int>.Shared;
        _pairBudget = pairBudget;
    }

    /// <summary>The number of retained triples.</summary>
    public int Count => _count;

    /// <summary>True once the state has spilled into rented arrays.</summary>
    public bool HasSpilled => _pairs is not null;

    /// <summary>
    /// Records <c>(kind, x, y)</c> and returns true if it was not entered before. Returns false if the triple is already in
    /// progress or already known equal; the caller then returns true, the coinductive assumption.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryEnter(int kind, object x, object y)
    {
        RuntimeHelpers.EnsureSufficientExecutionStack();

        if (_pairs is null)
        {
            if (ScanInline(kind, x, y))
            {
                return false;
            }

            // The budget applies only to a novel insertion. A memo hit at the boundary must still succeed.
            if (_count == _pairBudget)
            {
                ThrowBudgetExceeded();
            }

            if (_count < InlineCapacity)
            {
                SetInline(_count++, new ReferencePair(kind, x, y));
                return true;
            }
        }

        return TryEnterSlow(kind, x, y);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TryEnterSlow(int kind, object x, object y)
    {
        if (_pairs is null)
        {
            Spill();
        }

        ReferencePair pair = new ReferencePair(kind, x, y);
        int slot = Probe(pair, out bool found);
        if (found)
        {
            return false;
        }

        if (_count == _pairBudget)
        {
            ThrowBudgetExceeded();
        }

        if (_count == _pairsCapacity)
        {
            Grow();
            slot = Probe(pair, out _);
        }

        _pairs![_count] = pair;
        _index![slot] = ++_count;
        return true;
    }

    /// <summary>Position before a trial comparison. An int, no allocation.</summary>
    public int Mark() => _count;

    /// <summary>
    /// Forgets every pair entered since <paramref name="mark"/>. Used only by search-style comparisons, where a false
    /// result from one trial is not terminal.
    /// </summary>
    public void Rollback(int mark)
    {
        if (mark < 0 || mark > _count)
        {
            throw new ArgumentOutOfRangeException(nameof(mark));
        }

        if (_pairs is null)
        {
            _count = mark;                        // inline slots beyond _count are never read
            return;
        }

        // Pairs are removed in reverse insertion order, so clearing each one's index slot restores exactly the table
        // that existed before it was inserted. No tombstones are needed.
        for (int i = _count - 1; i >= mark; i--)
        {
            _index![SlotOf(i)] = 0;
            _pairs[i] = default;
        }

        _count = mark;
    }

    /// <summary>Returns the rented arrays. Called from the entry point's finally block; a no-op if the state never spilled.</summary>
    public void Dispose()
    {
        if (_pairs is not null)
        {
            ReferencePair[] pairs = _pairs;
            int[] index = _index!;
            _pairs = null;
            _index = null;
            _pairsCapacity = 0;
            _indexCapacity = 0;
            _pairPool.Return(pairs, clearArray: true);   // holds object references
            _indexPool.Return(index);                    // not cleared here: every rent clears before use
        }
    }

    // ----- inline buffer ----------------------------------------------------------------------------------------------

    private bool ScanInline(int kind, object x, object y)
    {
#if NET8_0_OR_GREATER
        ReadOnlySpan<ReferencePair> seen = ((ReadOnlySpan<ReferencePair>)_inline).Slice(0, _count);
        foreach (ref readonly ReferencePair p in seen)
        {
            if (p.Kind == kind && ReferenceEquals(p.X, x) && ReferenceEquals(p.Y, y))
            {
                return true;
            }
        }

        return false;
#else
        int count = _count;
        return (count > 0 && Matches(in _i0, kind, x, y))
            || (count > 1 && Matches(in _i1, kind, x, y))
            || (count > 2 && Matches(in _i2, kind, x, y))
            || (count > 3 && Matches(in _i3, kind, x, y))
            || (count > 4 && Matches(in _i4, kind, x, y))
            || (count > 5 && Matches(in _i5, kind, x, y))
            || (count > 6 && Matches(in _i6, kind, x, y))
            || (count > 7 && Matches(in _i7, kind, x, y));
#endif
    }

#if !NET8_0_OR_GREATER
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool Matches(in ReferencePair p, int kind, object x, object y)
        => p.Kind == kind && ReferenceEquals(p.X, x) && ReferenceEquals(p.Y, y);
#endif

    private ReferencePair GetInline(int i)
    {
#if NET8_0_OR_GREATER
        return _inline[i];
#else
        switch (i)
        {
            case 0: return _i0;
            case 1: return _i1;
            case 2: return _i2;
            case 3: return _i3;
            case 4: return _i4;
            case 5: return _i5;
            case 6: return _i6;
            default: return _i7;
        }
#endif
    }

    private void SetInline(int i, ReferencePair value)
    {
#if NET8_0_OR_GREATER
        _inline[i] = value;
#else
        switch (i)
        {
            case 0: _i0 = value; break;
            case 1: _i1 = value; break;
            case 2: _i2 = value; break;
            case 3: _i3 = value; break;
            case 4: _i4 = value; break;
            case 5: _i5 = value; break;
            case 6: _i6 = value; break;
            default: _i7 = value; break;
        }
#endif
    }

    // ----- spilled storage --------------------------------------------------------------------------------------------

    /// <summary>Rents the journal and index for the first time and copies the inline pairs in order.</summary>
    private void Spill()
    {
        int capacity = Math.Min(InitialSpillCapacity, _pairBudget);
        int indexCapacity = IndexCapacityFor(capacity);
        ReferencePair[]? pairs = null;
        int[]? index = null;
        bool published = false;
        try
        {
            pairs = _pairPool.Rent(capacity);
            index = _indexPool.Rent(indexCapacity);
            Array.Clear(index, 0, indexCapacity);
            int count = _count;
            for (int i = 0; i < count; i++)
            {
                ReferencePair p = GetInline(i);
                pairs[i] = p;
                index[FindEmptySlot(index, indexCapacity, p)] = i + 1;
            }

            _pairs = pairs;
            _index = index;
            _pairsCapacity = capacity;
            _indexCapacity = indexCapacity;
            published = true;
        }
        finally
        {
            if (!published)
            {
                if (pairs is not null)
                {
                    _pairPool.Return(pairs, clearArray: true);
                }

                if (index is not null)
                {
                    _indexPool.Return(index);
                }
            }
        }
    }

    /// <summary>Doubles the logical capacity up to the budget, re-indexing the journal in insertion order.</summary>
    private void Grow()
    {
        int newCapacity = (int)Math.Min(checked(2L * _pairsCapacity), _pairBudget);
        int newIndexCapacity = IndexCapacityFor(newCapacity);
        ReferencePair[] oldPairs = _pairs!;
        int[] oldIndex = _index!;
        ReferencePair[]? pairs = null;
        int[]? index = null;
        bool published = false;
        try
        {
            pairs = _pairPool.Rent(newCapacity);
            index = _indexPool.Rent(newIndexCapacity);
            Array.Clear(index, 0, newIndexCapacity);
            int count = _count;
            Array.Copy(oldPairs, pairs, count);
            for (int i = 0; i < count; i++)
            {
                index[FindEmptySlot(index, newIndexCapacity, pairs[i])] = i + 1;
            }

            _pairs = pairs;
            _index = index;
            _pairsCapacity = newCapacity;
            _indexCapacity = newIndexCapacity;
            published = true;
        }
        finally
        {
            if (published)
            {
                _pairPool.Return(oldPairs, clearArray: true);
                _indexPool.Return(oldIndex);
            }
            else
            {
                if (pairs is not null)
                {
                    _pairPool.Return(pairs, clearArray: true);
                }

                if (index is not null)
                {
                    _indexPool.Return(index);
                }
            }
        }
    }

    /// <summary>The smallest power of two at or above twice <paramref name="pairCapacity"/>, computed with checked arithmetic.</summary>
    private static int IndexCapacityFor(int pairCapacity)
    {
        long wanted = checked(2L * pairCapacity);
        long capacity = 1;
        while (capacity < wanted)
        {
            capacity = checked(capacity * 2);
        }

        return checked((int)capacity);
    }

    /// <summary>Linear probe from the pair's identity hash to the first empty slot. Used only while rebuilding a table from distinct pairs.</summary>
    private static int FindEmptySlot(int[] index, int indexCapacity, ReferencePair pair)
    {
        int mask = indexCapacity - 1;
        int slot = pair.GetHashCode() & mask;
        while (index[slot] != 0)
        {
            slot = (slot + 1) & mask;
        }

        return slot;
    }

    /// <summary>Linear probe for <paramref name="pair"/>: returns its slot if found, otherwise the empty slot where it would go.</summary>
    private int Probe(ReferencePair pair, out bool found)
    {
        int[] index = _index!;
        ReferencePair[] pairs = _pairs!;
        int mask = _indexCapacity - 1;
        int slot = pair.GetHashCode() & mask;
        while (true)
        {
            int value = index[slot];
            if (value == 0)
            {
                found = false;
                return slot;
            }

            if (pairs[value - 1].Equals(pair))
            {
                found = true;
                return slot;
            }

            slot = (slot + 1) & mask;
        }
    }

    /// <summary>The index slot holding journal entry <paramref name="i"/>.</summary>
    private int SlotOf(int i)
    {
        int[] index = _index!;
        int mask = _indexCapacity - 1;
        int slot = _pairs![i].GetHashCode() & mask;
        int wanted = i + 1;
        while (index[slot] != wanted)
        {
            slot = (slot + 1) & mask;
        }

        return slot;
    }

    private void ThrowBudgetExceeded() => throw new DeepEqualsComplexityException(_count);
}
