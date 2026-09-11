// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

namespace DeepEquals.SourceGeneration.Framework;

/// <summary>Hash-only callback into a generated core. Implemented by a generated struct so the call devirtualizes.</summary>
public interface IDeepEqualsHashOps<in T>
{
    int GetHashCode(T x);
}

/// <summary>Hash-only callback into a generated 64-bit core.</summary>
public interface IDeepEqualsHashOps64<in T>
{
    ulong GetHashCode64(T x);
}

/// <summary>Equality and hash callbacks into generated cores whose element closure needs comparison state.</summary>
public interface IDeepEqualsElementOps<in T> : IDeepEqualsHashOps<T>
{
    bool Equals(T x, T y, ref DeepEqualsState state);
}

/// <summary>Equality and hash callbacks into generated cores whose element closure is wholly acyclic.</summary>
public interface IDeepEqualsStatelessElementOps<in T> : IDeepEqualsHashOps<T>
{
    bool Equals(T x, T y);
}
