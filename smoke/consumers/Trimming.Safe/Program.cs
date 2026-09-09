// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;

namespace DeepEquals.Smoke.Trimming
{
    public static class Program
    {
        public static int Main()
        {
            bool equal = TrimmingContext.Safe.Equals(new Safe(1) { Shown = 2 }, new Safe(1) { Shown = 2 });
            Console.WriteLine(equal ? "OK" : "FAIL");
            return equal ? 0 : 1;
        }
    }
}
