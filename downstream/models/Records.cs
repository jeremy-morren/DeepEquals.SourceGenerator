// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

// Records and record structs, nested in each other. Their compiler-synthesized equality is the built-in the
// benchmarks measure the generated comparers against. Compiled only where the language version allows records;
// the consumer tiers pinned to C# 7.3 and 8 leave this file empty.

// ReSharper disable All

#if RECORDS

using System;

namespace DeepEquals.Downstream
{
    public readonly record struct Money(decimal Amount, string Currency);

    public readonly record struct Point3(double X, double Y, double Z);

    public sealed record Address(string Street, string City, int Zip);

    /// <summary>A record holding a record and a record struct.</summary>
    public sealed record Invoice(long Id, Address BillTo, Money Total, string Notes);

    /// <summary>A record holding a record, a record struct and a nullable record struct.</summary>
    public sealed record Person(string Name, Address Home, Point3 Position, Money? Balance);

    /// <summary>Record structs in sequences: a bit-block struct behind an array and behind a list that is not one.</summary>
    public sealed class PointArrays
    {
        public Point3[] Points { get; set; }
        public System.Collections.Generic.IReadOnlyList<Point3> View { get; set; }
    }
}

#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    /// <summary>Lets the compiler emit <c>init</c> accessors on frameworks that predate the type.</summary>
    internal static class IsExternalInit
    {
    }
}
#endif

#endif
