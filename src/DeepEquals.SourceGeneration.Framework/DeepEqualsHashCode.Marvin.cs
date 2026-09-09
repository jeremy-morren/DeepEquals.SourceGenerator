// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

#if NETSTANDARD2_0
using System;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace DeepEquals.SourceGeneration.Framework;

public static partial class DeepEqualsHashCode
{
    private static ulong s_marvinSeed = GenerateMarvinSeed();

    /// <summary>The Marvin seed used by <see cref="Hash(string)"/> on this asset. Settable from the test assembly only.</summary>
    internal static ulong MarvinSeed
    {
        get => s_marvinSeed;
        set => s_marvinSeed = value;
    }

    private static ulong GenerateMarvinSeed()
    {
        byte[] bytes = new byte[8];
        using (RandomNumberGenerator rng = RandomNumberGenerator.Create())
        {
            rng.GetBytes(bytes);
        }
        return BitConverter.ToUInt64(bytes, 0);
    }

    /// <summary>
    /// Marvin32 over the UTF-16 code units of <paramref name="value"/>
    /// </summary>
    /// <remarks>
    /// This is the algorithm .NET Core uses for randomized string hashing,
    /// re-implemented here to provide this behavior on .NET Framework.
    /// </remarks>
    private static int Marvin(string value)
    {
        unchecked
        {
            ulong seed = s_marvinSeed;
            uint p0 = (uint)seed;
            uint p1 = (uint)(seed >> 32);
            int length = value.Length;
            int i = 0;

            // Eight bytes, four code units, per iteration.
            while (length - i >= 4)
            {
                p0 += value[i] | ((uint)value[i + 1] << 16);
                MarvinBlock(ref p0, ref p1);
                p0 += value[i + 2] | ((uint)value[i + 3] << 16);
                MarvinBlock(ref p0, ref p1);
                i += 4;
            }

            // The byte count is even, so only the 0, 2, 4 and 6 remaining-byte cases of the reference algorithm occur.
            switch (length - i)
            {
                case 3:
                    p0 += value[i] | ((uint)value[i + 1] << 16);
                    MarvinBlock(ref p0, ref p1);
                    p0 += 0x800000u | value[i + 2];
                    break;
                case 2:
                    p0 += value[i] | ((uint)value[i + 1] << 16);
                    MarvinBlock(ref p0, ref p1);
                    p0 += 0x80u;
                    break;
                case 1:
                    p0 += 0x800000u | value[i];
                    break;
                default:
                    p0 += 0x80u;
                    break;
            }

            MarvinBlock(ref p0, ref p1);
            MarvinBlock(ref p0, ref p1);
            return (int)(p1 ^ p0);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void MarvinBlock(ref uint rp0, ref uint rp1)
    {
        unchecked
        {
            uint p0 = rp0;
            uint p1 = rp1;

            p1 ^= p0;
            p0 = RotateLeft(p0, 20);

            p0 += p1;
            p1 = RotateLeft(p1, 9);

            p1 ^= p0;
            p0 = RotateLeft(p0, 27);

            p0 += p1;
            p1 = RotateLeft(p1, 19);

            rp0 = p0;
            rp1 = p1;
        }
    }
}
#endif
