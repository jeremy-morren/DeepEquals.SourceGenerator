// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Runtime.CompilerServices;

// ReSharper disable UnusedMember.Global

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
    /// </summary>
    /// <remarks>
    /// A deliberate dependency on the runtime layout, verified by tests on every supported runtime.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ulong DateTimeBits(DateTime value) => Unsafe.As<DateTime, ulong>(ref value);

    /// <summary>
    /// Bitwise equality of two decimals: the same 96-bit magnitude, scale and sign, so <c>1.0m</c> and <c>1.00m</c>
    /// differ. Two 64-bit compares over the struct's storage, the same test <c>decimal.GetBits</c> word by word makes.
    /// </summary>
    /// <remarks>Relies only on <c>sizeof(decimal) == 16</c> with no padding, which every runtime guarantees.</remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool DecimalEquals(in decimal x, in decimal y)
    {
        ref var a = ref Unsafe.As<decimal, ulong>(ref Unsafe.AsRef(in x));
        ref var b = ref Unsafe.As<decimal, ulong>(ref Unsafe.AsRef(in y));
        return a == b && Unsafe.Add(ref a, 1) == Unsafe.Add(ref b, 1);
    }

    /// <summary>One of the four 32-bit words of a decimal's storage, for hashing every bit the equality above compares.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int DecimalWord(in decimal value, int index) => Unsafe.Add(ref Unsafe.As<decimal, int>(ref Unsafe.AsRef(in value)), index);

    /// <summary>One of the four 32-bit words of a <see cref="Guid"/>; <see cref="Guid.GetHashCode"/> ignores six of its bytes.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GuidWord(in Guid value, int index) => Unsafe.Add(ref Unsafe.As<Guid, int>(ref Unsafe.AsRef(in value)), index);

    /// <summary>
    /// <c>x.Equals(y)</c> through the <see cref="IEquatable{T}"/> constraint: a constrained call,
    /// direct for a struct even when the implementation is explicit, and an interface call for a class.
    /// Exactly what <c>EqualityComparer&lt;T&gt;.Default</c> does for such a <typeparamref name="T"/> after its null checks,
    /// without the comparer lookup and virtual call, which matter on runtimes that do not devirtualize the default comparer.
    /// Callers have already applied the null rule.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool EquatableEquals<T>(T x, T y) where T : IEquatable<T> => x.Equals(y);

    /// <summary>A writable reference to an <c>in</c> parameter, for field accessors that take their receiver by <c>ref</c>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref T AsWritableRef<T>(in T value) => ref Unsafe.AsRef(in value);

#if NET7_0_OR_GREATER
    /// <summary>A read-only reference to the payload of a nullable, or to its default, without copying a large struct.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref readonly T GetNullableValueRefOrDefaultRef<T>(in T? value) where T : struct
        => ref Nullable.GetValueRefOrDefaultRef(in value);
#endif
}
