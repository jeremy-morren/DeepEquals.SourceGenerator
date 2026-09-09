// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;

namespace DeepEquals.Smoke
{
    /// <summary>
    /// Present on the netstandard legs too, where it is never called; those exist to prove the
    /// generated code compiles against that reference surface.
    /// </summary>
    public static class Program
    {
        public static int Main()
        {
            string result = Checks.Run();
            Console.WriteLine(result);
            return result.StartsWith("OK", StringComparison.Ordinal) ? 0 : 1;
        }
    }
}
