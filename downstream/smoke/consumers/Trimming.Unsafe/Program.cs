// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;

namespace DeepEquals.Smoke.Trimming;

public static class Program
{
    public static int Main()
    {
        // The line the trimming warnings must point at: the consumer's own use of a comparer that
        // reaches a generic holder's private field.
        var equal = TrimmingContext.Risky.Equals(
            new Risky { Inner = new Box<int>(1) },
            new Risky { Inner = new Box<int>(1) });
        Console.WriteLine(equal ? "OK" : "FAIL");
        return equal ? 0 : 1;
    }
}