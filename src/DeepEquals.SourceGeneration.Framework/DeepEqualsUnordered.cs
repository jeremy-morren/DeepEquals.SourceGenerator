// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace DeepEquals.SourceGeneration.Framework;

/// <summary>
/// Unordered multiset equality for sets and dictionaries under the library's own element equality. Both sides are
/// materialized and hashed once, sorted by <c>(hash, index)</c>, merged run by run, and entries inside a hash-collision
/// run are matched exactly by augmenting paths, so equality is symmetric and independent of enumeration order.
/// </summary>
public static class DeepEqualsUnordered
{
    // ----- public entry points ----------------------------------------------------------------------------------------

    /// <summary>Set equality for elements whose closure is cyclic; trials mark and roll back <paramref name="state"/>.</summary>
    public static bool SetEquals<T, TOps>(IEnumerable<T> x, IEnumerable<T> y, int count, int maxCollisionRun, ref DeepEqualsState state)
        where TOps : struct, IDeepEqualsElementOps<T>
        => SetEqualsCore<T, StatefulOps<T, TOps>>(x, y, count, maxCollisionRun, ref state);

    /// <summary>Set equality for elements whose closure is wholly acyclic; no state, mark or rollback.</summary>
    public static bool SetEquals<T, TOps>(IEnumerable<T> x, IEnumerable<T> y, int count, int maxCollisionRun)
        where TOps : struct, IDeepEqualsStatelessElementOps<T>
    {
        DeepEqualsState none = default;
        return SetEqualsCore<T, StatelessOps<T, TOps>>(x, y, count, maxCollisionRun, ref none);
    }

    /// <summary>Dictionary equality over key-value pairs whose closure is cyclic.</summary>
    public static bool DictionaryEquals<TKey, TValue, TOps>(IEnumerable<KeyValuePair<TKey, TValue>> x, IEnumerable<KeyValuePair<TKey, TValue>> y, int count, int maxCollisionRun, ref DeepEqualsState state)
        where TKey : notnull
        where TOps : struct, IDeepEqualsElementOps<KeyValuePair<TKey, TValue>>
        => DictionaryEqualsCore<TKey, TValue, StatefulOps<KeyValuePair<TKey, TValue>, TOps>>(x, y, count, maxCollisionRun, ref state);

    /// <summary>Dictionary equality over key-value pairs whose closure is wholly acyclic.</summary>
    public static bool DictionaryEquals<TKey, TValue, TOps>(IEnumerable<KeyValuePair<TKey, TValue>> x, IEnumerable<KeyValuePair<TKey, TValue>> y, int count, int maxCollisionRun)
        where TKey : notnull
        where TOps : struct, IDeepEqualsStatelessElementOps<KeyValuePair<TKey, TValue>>
    {
        DeepEqualsState none = default;
        return DictionaryEqualsCore<TKey, TValue, StatelessOps<KeyValuePair<TKey, TValue>, TOps>>(x, y, count, maxCollisionRun, ref none);
    }

    // ----- ops adapters -----------------------------------------------------------------------------------------------

    internal interface IUnorderedOps<T>
    {
        bool IsStateful { get; }
        bool Equals(T x, T y, ref DeepEqualsState state);
        int GetHashCode(T x);
    }

    internal readonly struct StatefulOps<T, TOps> : IUnorderedOps<T>
        where TOps : struct, IDeepEqualsElementOps<T>
    {
        public bool IsStateful => true;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(T x, T y, ref DeepEqualsState state) => default(TOps).Equals(x, y, ref state);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHashCode(T x) => default(TOps).GetHashCode(x);
    }

    internal readonly struct StatelessOps<T, TOps> : IUnorderedOps<T>
        where TOps : struct, IDeepEqualsStatelessElementOps<T>
    {
        public bool IsStateful => false;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(T x, T y, ref DeepEqualsState state) => default(TOps).Equals(x, y);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHashCode(T x) => default(TOps).GetHashCode(x);
    }

    // ----- materialization --------------------------------------------------------------------------------------------

    private static bool SetEqualsCore<T, TW>(IEnumerable<T> x, IEnumerable<T> y, int count, int maxCollisionRun, ref DeepEqualsState state)
        where TW : struct, IUnorderedOps<T>
    {
        if (x is null) throw new ArgumentNullException(nameof(x));
        if (y is null) throw new ArgumentNullException(nameof(y));
        ValidateArguments(count, maxCollisionRun);
        if (count == 0)
        {
            return true;
        }

        ArrayPool<T> entryPool = DeepEqualsPools<T>.Shared;
        ArrayPool<long> keyPool = DeepEqualsPools<long>.Shared;
        T[]? xs = null;
        long[]? xk = null;
        T[]? ys = null;
        long[]? yk = null;
        try
        {
            xs = entryPool.Rent(count);
            xk = keyPool.Rent(count);
            ys = entryPool.Rent(count);
            yk = keyPool.Rent(count);
            int xSum = FillSet<T, TW>(x, xs, xk, count);
            int ySum = FillSet<T, TW>(y, ys, yk, count);
            if (xSum != ySum)
            {
                return false;
            }

            return Match<T, TW>(xs, xk, ys, yk, count, maxCollisionRun, x.GetType(), ref state);
        }
        finally
        {
            ReturnEntries(entryPool, xs);
            ReturnEntries(entryPool, ys);
            if (xk is not null) keyPool.Return(xk);
            if (yk is not null) keyPool.Return(yk);
        }
    }

    private static bool DictionaryEqualsCore<TKey, TValue, TW>(IEnumerable<KeyValuePair<TKey, TValue>> x, IEnumerable<KeyValuePair<TKey, TValue>> y, int count, int maxCollisionRun, ref DeepEqualsState state)
        where TKey : notnull
        where TW : struct, IUnorderedOps<KeyValuePair<TKey, TValue>>
    {
        if (x is null) throw new ArgumentNullException(nameof(x));
        if (y is null) throw new ArgumentNullException(nameof(y));
        ValidateArguments(count, maxCollisionRun);
        if (count == 0)
        {
            return true;
        }

        ArrayPool<KeyValuePair<TKey, TValue>> entryPool = DeepEqualsPools<KeyValuePair<TKey, TValue>>.Shared;
        ArrayPool<long> keyPool = DeepEqualsPools<long>.Shared;
        KeyValuePair<TKey, TValue>[]? xs = null;
        long[]? xk = null;
        KeyValuePair<TKey, TValue>[]? ys = null;
        long[]? yk = null;
        try
        {
            xs = entryPool.Rent(count);
            xk = keyPool.Rent(count);
            ys = entryPool.Rent(count);
            yk = keyPool.Rent(count);
            int xSum = FillDictionary<TKey, TValue, TW>(x, xs, xk, count);
            int ySum = FillDictionary<TKey, TValue, TW>(y, ys, yk, count);
            if (xSum != ySum)
            {
                return false;
            }

            return Match<KeyValuePair<TKey, TValue>, TW>(xs, xk, ys, yk, count, maxCollisionRun, x.GetType(), ref state);
        }
        finally
        {
            ReturnEntries(entryPool, xs);
            ReturnEntries(entryPool, ys);
            if (xk is not null) keyPool.Return(xk);
            if (yk is not null) keyPool.Return(yk);
        }
    }

    private static void ValidateArguments(int count, int maxCollisionRun)
    {
        if (count < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "The advertised count cannot be negative.");
        }

        if (maxCollisionRun < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCollisionRun), maxCollisionRun, "The collision run cap must be at least 1.");
        }
    }

    /// <summary>Materializes a set side: entries, packed <c>(hash, index)</c> keys, and the unchecked hash sum.</summary>
    private static int FillSet<T, TW>(IEnumerable<T> source, T[] entries, long[] keys, int count)
        where TW : struct, IUnorderedOps<T>
    {
        int n = 0;
        int sum = 0;
        if (source is HashSet<T> set)
        {
            foreach (T element in set)
            {
                if (n == count) ThrowCountMismatch(source, count, tooMany: true);
                int hash = default(TW).GetHashCode(element);
                entries[n] = element;
                keys[n] = Pack(hash, n);
                unchecked { sum += hash; }
                n++;
            }
        }
        else
        {
            foreach (T element in source)
            {
                if (n == count) ThrowCountMismatch(source, count, tooMany: true);
                int hash = default(TW).GetHashCode(element);
                entries[n] = element;
                keys[n] = Pack(hash, n);
                unchecked { sum += hash; }
                n++;
            }
        }

        if (n != count) ThrowCountMismatch(source, count, tooMany: false);
        return sum;
    }

    /// <summary>Materializes a dictionary side through the struct enumerator when the instance is a <see cref="Dictionary{TKey, TValue}"/>.</summary>
    private static int FillDictionary<TKey, TValue, TW>(IEnumerable<KeyValuePair<TKey, TValue>> source, KeyValuePair<TKey, TValue>[] entries, long[] keys, int count)
        where TKey : notnull
        where TW : struct, IUnorderedOps<KeyValuePair<TKey, TValue>>
    {
        int n = 0;
        int sum = 0;
        if (source is Dictionary<TKey, TValue> dictionary)
        {
            foreach (KeyValuePair<TKey, TValue> entry in dictionary)
            {
                if (n == count) ThrowCountMismatch(source, count, tooMany: true);
                int hash = default(TW).GetHashCode(entry);
                entries[n] = entry;
                keys[n] = Pack(hash, n);
                unchecked { sum += hash; }
                n++;
            }
        }
        else
        {
            foreach (KeyValuePair<TKey, TValue> entry in source)
            {
                if (n == count) ThrowCountMismatch(source, count, tooMany: true);
                int hash = default(TW).GetHashCode(entry);
                entries[n] = entry;
                keys[n] = Pack(hash, n);
                unchecked { sum += hash; }
                n++;
            }
        }

        if (n != count) ThrowCountMismatch(source, count, tooMany: false);
        return sum;
    }

    private static void ThrowCountMismatch(object source, int count, bool tooMany)
        => throw new InvalidOperationException(
            $"{source.GetType()} advertised a Count of {count} but enumerated {(tooMany ? "more" : "fewer")} entries. Collections must report their true count.");

    private static void ReturnEntries<T>(ArrayPool<T> pool, T[]? entries)
    {
        if (entries is not null)
        {
#if NETSTANDARD2_0
            pool.Return(entries, clearArray: true);
#else
            pool.Return(entries, clearArray: RuntimeHelpers.IsReferenceOrContainsReferences<T>());
#endif
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Pack(int hash, int index) => ((long)hash << 32) | (uint)index;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int HashOf(long key) => unchecked((int)(key >> 32));

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int IndexOf(long key) => unchecked((int)key);

    // ----- matching ---------------------------------------------------------------------------------------------------

    private static bool Match<T, TW>(T[] xs, long[] xk, T[] ys, long[] yk, int count, int maxCollisionRun, Type collectionType, ref DeepEqualsState state)
        where TW : struct, IUnorderedOps<T>
    {
        Array.Sort(xk, 0, count);
        Array.Sort(yk, 0, count);

        int i = 0;
        while (i < count)
        {
            int hash = HashOf(xk[i]);
            if (HashOf(yk[i]) != hash)
            {
                return false;
            }

            int xEnd = i + 1;
            while (xEnd < count && HashOf(xk[xEnd]) == hash) xEnd++;
            int yEnd = i + 1;
            while (yEnd < count && HashOf(yk[yEnd]) == hash) yEnd++;
            int k = xEnd - i;
            if (k != yEnd - i)
            {
                return false;
            }

            if (k == 1)
            {
                // The common case: one direct comparison decides. True keeps whatever pairs it entered; false is terminal.
                if (!default(TW).Equals(xs[IndexOf(xk[i])], ys[IndexOf(yk[i])], ref state))
                {
                    return false;
                }
            }
            else
            {
                if (k > maxCollisionRun)
                {
                    throw new DeepEqualsComplexityException(collectionType, k, maxCollisionRun);
                }

                if (!MatchRun<T, TW>(xs, xk, ys, yk, i, k, ref state))
                {
                    return false;
                }
            }

            i = xEnd;
        }

        return true;
    }

    /// <summary>Exact multiset matching inside one hash-collision run of length <paramref name="k"/> on both sides.</summary>
    private static bool MatchRun<T, TW>(T[] xs, long[] xk, T[] ys, long[] yk, int start, int k, ref DeepEqualsState state)
        where TW : struct, IUnorderedOps<T>
    {
        ArrayPool<ulong> bitPool = DeepEqualsPools<ulong>.Shared;
        ArrayPool<int> intPool = DeepEqualsPools<int>.Shared;
        ulong[]? matrix = null;
        int[]? workspace = null;
        try
        {
            int words = (k * k + 63) >> 6;
            matrix = bitPool.Rent(words);
            Array.Clear(matrix, 0, words);

            bool stateful = default(TW).IsStateful;
            for (int a = 0; a < k; a++)
            {
                T xa = xs[IndexOf(xk[start + a])];
                for (int b = 0; b < k; b++)
                {
                    T yb = ys[IndexOf(yk[start + b])];
                    bool equal;
                    if (stateful)
                    {
                        // Every trial rolls back, whether it returned true or false, so no trial's assumptions leak into another.
                        int mark = state.Mark();
                        equal = default(TW).Equals(xa, yb, ref state);
                        state.Rollback(mark);
                    }
                    else
                    {
                        equal = default(TW).Equals(xa, yb, ref state);
                    }

                    if (equal)
                    {
                        int bit = a * k + b;
                        matrix[bit >> 6] |= 1UL << (bit & 63);
                    }
                }
            }

            // Workspace layout: matchR [0, k), visited stamps [k, 2k), stack left [2k, 3k), stack next [3k, 4k), stack chosen [4k, 5k).
            workspace = intPool.Rent(5 * k);
            for (int r = 0; r < k; r++)
            {
                workspace[r] = -1;
                workspace[k + r] = 0;
            }

            for (int u = 0; u < k; u++)
            {
                if (!Augment(matrix, workspace, k, u, u + 1))
                {
                    return false;
                }
            }

            if (stateful)
            {
                // Commit: run each matched pair once more without rollback so its pairs land in the state.
                for (int r = 0; r < k; r++)
                {
                    int l = workspace[r];
                    if (!default(TW).Equals(xs[IndexOf(xk[start + l])], ys[IndexOf(yk[start + r])], ref state))
                    {
                        return false;   // defensive: an unstable comparer changed its answer on commit
                    }
                }
            }

            return true;
        }
        finally
        {
            if (matrix is not null) bitPool.Return(matrix);
            if (workspace is not null) intPool.Return(workspace);
        }
    }

    /// <summary>Kuhn's augmenting-path search from left vertex <paramref name="root"/>, iterative over the rented stack.</summary>
    private static bool Augment(ulong[] matrix, int[] ws, int k, int root, int stamp)
    {
        int visited = k;
        int stackLeft = 2 * k;
        int stackNext = 3 * k;
        int stackChosen = 4 * k;

        int depth = 0;
        ws[stackLeft] = root;
        ws[stackNext] = 0;
        while (depth >= 0)
        {
            int v = ws[stackLeft + depth];
            int r = ws[stackNext + depth];
            int row = v * k;
            while (r < k && (!IsSet(matrix, row + r) || ws[visited + r] == stamp))
            {
                r++;
            }

            if (r == k)
            {
                depth--;
                continue;
            }

            ws[visited + r] = stamp;
            ws[stackNext + depth] = r + 1;
            ws[stackChosen + depth] = r;
            int owner = ws[r];
            if (owner < 0)
            {
                for (int d = depth; d >= 0; d--)
                {
                    ws[ws[stackChosen + d]] = ws[stackLeft + d];
                }

                return true;
            }

            depth++;
            ws[stackLeft + depth] = owner;
            ws[stackNext + depth] = 0;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsSet(ulong[] matrix, int bit) => (matrix[bit >> 6] & (1UL << (bit & 63))) != 0;
}
