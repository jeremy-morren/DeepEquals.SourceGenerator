// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Runtime.CompilerServices;

namespace DeepEquals.SourceGeneration.Framework;

/// <summary>Pinned shims around APIs whose overload sets vary by language or framework version, so generated code binds one stable form.</summary>
public static class DeepEqualsHelpers
{
#if NETSTANDARD2_0
    /// <summary>The bits of a <see cref="float"/>; <c>BitConverter.SingleToInt32Bits</c> arrived in netstandard2.1.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int SingleToInt32Bits(float value) => Unsafe.As<float, int>(ref value);
#endif

    /// <summary>
    /// The complete 64-bit storage of a <see cref="DateTime"/>: ticks, kind and the hidden ambiguous-daylight-saving state.
    /// A deliberate dependency on the runtime layout, verified by tests on every supported runtime.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong DateTimeBits(DateTime value) => Unsafe.As<DateTime, ulong>(ref value);

    /// <summary>
    /// <c>x.Equals(y)</c> through the <see cref="IEquatable{T}"/> constraint: a constrained call, direct for a struct even when the
    /// implementation is explicit, and an interface call for a class. Exactly what <c>EqualityComparer&lt;T&gt;.Default</c> does for such a
    /// <typeparamref name="T"/> after its null checks, without the comparer lookup and virtual call, which matter on runtimes that do not
    /// devirtualize the default comparer. Callers have already applied the null rule.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool EquatableEquals<T>(T x, T y)
        where T : IEquatable<T>
        => x.Equals(y);

    /// <summary>A writable reference to an <c>in</c> parameter, for field accessors that take their receiver by <c>ref</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref T AsWritableRef<T>(in T value) => ref Unsafe.AsRef(in value);

#if NET7_0_OR_GREATER
    /// <summary>A read-only reference to the payload of a nullable, or to its default, without copying a large struct.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref readonly T GetNullableValueRefOrDefaultRef<T>(in T? value)
        where T : struct
        => ref Nullable.GetValueRefOrDefaultRef(in value);
#endif
}
