// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using DeepEquals.Downstream;

namespace DeepEquals.Smoke
{
    /// <summary>
    /// Present on the netstandard legs too, where it is never called; those exist to prove the
    /// generated code compiles against that reference surface.
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--cold")
            {
                // Before anything else touches a context, so the first call pays every one-time cost.
                Console.WriteLine(Checks.Describe());
                Console.WriteLine(MicroBench.ColdStart());
                return 0;
            }

            if (args.Length > 0 && args[0] == "--bench")
            {
                // The stopwatch harness over the shared scenarios: indicative numbers for this tier.
                Console.WriteLine(Checks.Describe());
                Console.WriteLine(MicroBench.Format(MicroBench.Run(Scenarios.All(), 100)));
                return 0;
            }

            var result = Checks.Run();
            Console.WriteLine(result);
            return result.StartsWith("OK", StringComparison.Ordinal) ? 0 : 1;
        }
    }
}
