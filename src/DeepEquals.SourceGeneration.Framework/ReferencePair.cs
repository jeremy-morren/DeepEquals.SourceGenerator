// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Runtime.CompilerServices;

namespace DeepEquals.SourceGeneration.Framework;

/// <summary>One retained memo triple: the guarded core's kind and the two object identities it entered.</summary>
public readonly struct ReferencePair(int kind, object x, object y) : IEquatable<ReferencePair>
{
    public readonly int Kind = kind;
    public readonly object X = x;
    public readonly object Y = y;

    public bool Equals(ReferencePair other)
        => Kind == other.Kind && ReferenceEquals(X, other.X) && ReferenceEquals(Y, other.Y);

    public override bool Equals(object? obj) => obj is ReferencePair p && Equals(p);

    // Internal identity mixing, not the public value hash: it must not touch DeepEqualsHashCode, whose first use
    // initializes the random seed. Two multiplies and a xor over identity hashes is plenty for a probe table.
    public override int GetHashCode()
    {
        unchecked
        {
            int h = RuntimeHelpers.GetHashCode(X) * (int)0x9E3779B1;
            h ^= RuntimeHelpers.GetHashCode(Y) * 0x7FEB352D;
            return h ^ (Kind * 0x2545F491);
        }
    }
}
