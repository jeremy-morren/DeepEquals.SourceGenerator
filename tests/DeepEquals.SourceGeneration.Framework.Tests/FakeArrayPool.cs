using System;
using System.Buffers;
using System.Collections.Generic;

namespace DeepEquals.SourceGeneration.Framework.Tests;

/// <summary>
/// A pool that counts rents and returns, hands out oversized arrays prefilled with garbage, and can fail the Nth rent,
/// so initialization before use, clearing on return and exactly-once return can be asserted.
/// </summary>
internal sealed class FakeArrayPool<T> : ArrayPool<T>
{
    private readonly Func<T> _garbage;
    private readonly HashSet<T[]> _outstanding = new HashSet<T[]>(ReferenceEqualityComparer<T[]>.Instance);

    public FakeArrayPool(Func<T> garbage, int extraCapacity = 5)
    {
        _garbage = garbage;
        ExtraCapacity = extraCapacity;
    }

    public int ExtraCapacity { get; }

    public int Rents { get; private set; }

    public int Returns { get; private set; }

    public int Outstanding => _outstanding.Count;

    /// <summary>When positive, the rent with this 1-based ordinal throws.</summary>
    public int FailOnRent { get; set; }

    public List<T[]> Returned { get; } = new List<T[]>();

    public override T[] Rent(int minimumLength)
    {
        Rents++;
        if (Rents == FailOnRent)
        {
            throw new OutOfMemoryException("Simulated pool failure.");
        }

        T[] array = new T[minimumLength + ExtraCapacity];
        for (int i = 0; i < array.Length; i++)
        {
            array[i] = _garbage();
        }

        _outstanding.Add(array);
        return array;
    }

    public override void Return(T[] array, bool clearArray = false)
    {
        if (!_outstanding.Remove(array))
        {
            throw new InvalidOperationException("Returned an array this pool did not rent, or returned it twice.");
        }

        Returns++;
        if (clearArray)
        {
            Array.Clear(array, 0, array.Length);
        }

        Returned.Add(array);
    }

    private sealed class ReferenceEqualityComparer<TArray> : IEqualityComparer<TArray>
        where TArray : class
    {
        public static readonly ReferenceEqualityComparer<TArray> Instance = new ReferenceEqualityComparer<TArray>();

        public bool Equals(TArray? x, TArray? y) => ReferenceEquals(x, y);

        public int GetHashCode(TArray obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}

/// <summary>Installs fake pools for the duration of a test and restores the shared pools afterwards.</summary>
internal sealed class PoolScope : IDisposable
{
    private readonly ArrayPool<ReferencePair> _pairs;
    private readonly ArrayPool<int> _ints;
    private readonly ArrayPool<long> _longs;
    private readonly ArrayPool<ulong> _ulongs;

    public PoolScope()
    {
        _pairs = DeepEqualsPools<ReferencePair>.Shared;
        _ints = DeepEqualsPools<int>.Shared;
        _longs = DeepEqualsPools<long>.Shared;
        _ulongs = DeepEqualsPools<ulong>.Shared;

        Pairs = new FakeArrayPool<ReferencePair>(() => new ReferencePair(-1, new object(), new object()));
        Ints = new FakeArrayPool<int>(() => 0x5A5A5A5A);
        Longs = new FakeArrayPool<long>(() => 0x5A5A5A5A5A5A5A5A);
        ULongs = new FakeArrayPool<ulong>(() => ulong.MaxValue);

        DeepEqualsPools<ReferencePair>.Shared = Pairs;
        DeepEqualsPools<int>.Shared = Ints;
        DeepEqualsPools<long>.Shared = Longs;
        DeepEqualsPools<ulong>.Shared = ULongs;
    }

    public FakeArrayPool<ReferencePair> Pairs { get; }

    public FakeArrayPool<int> Ints { get; }

    public FakeArrayPool<long> Longs { get; }

    public FakeArrayPool<ulong> ULongs { get; }

    public void Dispose()
    {
        DeepEqualsPools<ReferencePair>.Shared = _pairs;
        DeepEqualsPools<int>.Shared = _ints;
        DeepEqualsPools<long>.Shared = _longs;
        DeepEqualsPools<ulong>.Shared = _ulongs;
    }
}
