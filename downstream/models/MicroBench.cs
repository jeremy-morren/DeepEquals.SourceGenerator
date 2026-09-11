// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

// A stopwatch harness for the places BenchmarkDotNet cannot go: the browser, and a consumer executable on any tier.
// It reports nanoseconds per call for each scenario's four operations. The numbers are indicative, not rigorous;
// the benchmarks project gives the rigorous ones on every platform it can host. Written to C# 7.3.

// ReSharper disable All

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace DeepEquals.Downstream
{
    public sealed class MicroResult
    {
        public string Name;
        public double GeneratedEqualsNs;
        public double BuiltInEqualsNs;
        public double GeneratedHashNs;
        public double BuiltInHashNs;

        /// <summary>The same hash under the 64-bit stream, or NaN where the scenario has no 64-bit root.</summary>
        public double Generated64HashNs;
    }

    public static class MicroBench
    {
        /// <summary>
        /// The first call per context, timed once in a fresh process: holder initialization, dispatch maps, bit-block size
        /// checks and the hashing library's type load, which is what a new process or a browser tab pays before any
        /// steady-state number applies. Returns one line per context.
        /// </summary>
        public static string ColdStart()
        {
            var text = new StringBuilder();
            text.AppendLine(Pad("cold start", 32) + "first Equals + GetHashCode");
            Cold(text, "Graph, XxHash32", () => DownstreamContext.Order.Equals(Data.Order(2), Data.Order(2)) && DownstreamContext.Order.GetHashCode(Data.Order(2)) != 0);
            Cold(text, "Graph, XxHash64", () => DownstreamHash64Context.Order.Equals(Data.Order(2), Data.Order(2)) && DownstreamHash64Context.Order.GetHashCode(Data.Order(2)) != 0);
            Cold(text, "Path", () => DownstreamPathContext.TreeNode.Equals(Data.Tree(6, 2, 4), Data.Tree(6, 2, 4)) && DownstreamPathContext.TreeNode.GetHashCode(Data.Tree(6, 2, 4)) != 0);
            Cold(text, "Tree", () => DownstreamTreeContext.TreeNode.Equals(Data.Tree(6, 2, 4), Data.Tree(6, 2, 4)) && DownstreamTreeContext.TreeNode.GetHashCode(Data.Tree(6, 2, 4)) != 0);
            Cold(text, "bit blocks", () => DownstreamContext.ArrayOfDouble.Equals(Data.Doubles(1, 64), Data.Doubles(1, 64)) && DownstreamContext.ArrayOfDouble.GetHashCode(Data.Doubles(1, 64)) != 0);
            return text.ToString();
        }

        private static void Cold(StringBuilder text, string name, Func<bool> first)
        {
            var watch = Stopwatch.StartNew();
            var ok = first();
            watch.Stop();
            text.AppendLine(Pad(name, 32) + (watch.ElapsedTicks * (1e6 / Stopwatch.Frequency)).ToString("N1", CultureInfo.InvariantCulture) + " us" + (ok ? string.Empty : " (unexpected result)"));
        }

        /// <summary>Consumed by every measured call, so no result is dead.</summary>
        public static int Sink;

        /// <summary>Times every operation of every scenario for roughly <paramref name="millisecondsPerCase"/> each.</summary>
        public static List<MicroResult> Run(Scenario[] scenarios, int millisecondsPerCase)
        {
            // Every operation runs well past the tiering threshold first, then the process goes quiet long enough for a
            // tiered JIT to finish promoting what it saw; without this, .NET measures partly unoptimized code.
            for (var pass = 0; pass < 2; pass++)
            {
                foreach (var scenario in scenarios)
                {
                    Warm(scenario.GeneratedEquals);
                    Warm(scenario.BuiltInEquals);
                    Warm(scenario.GeneratedHash);
                    Warm(scenario.BuiltInHash);
                    if (scenario.Generated64Hash != null) Warm(scenario.Generated64Hash);
                }

                Settle();
            }

            var results = new List<MicroResult>(scenarios.Length);
            foreach (var scenario in scenarios)
            {
                results.Add(new MicroResult
                {
                    Name = scenario.Name,
                    GeneratedEqualsNs = Measure(scenario.GeneratedEquals, millisecondsPerCase),
                    BuiltInEqualsNs = Measure(scenario.BuiltInEquals, millisecondsPerCase),
                    GeneratedHashNs = Measure(scenario.GeneratedHash, millisecondsPerCase),
                    BuiltInHashNs = Measure(scenario.BuiltInHash, millisecondsPerCase),
                    Generated64HashNs = scenario.Generated64Hash != null ? Measure(scenario.Generated64Hash, millisecondsPerCase) : double.NaN,
                });
            }

            return results;
        }

        /// <summary>A fixed-width table, one scenario per line, with the generated-over-built-in ratios.</summary>
        public static string Format(List<MicroResult> results)
        {
            var text = new StringBuilder();
            text.AppendLine(Pad("scenario", 48) + Pad("equals gen", 14) + Pad("equals builtin", 16) + Pad("ratio", 8) + Pad("hash gen", 14) + Pad("hash builtin", 16) + Pad("ratio", 8) + Pad("hash64 gen", 14) + "ratio");
            foreach (var r in results)
            {
                text.Append(Pad(r.Name, 48));
                text.Append(Pad(Ns(r.GeneratedEqualsNs), 14));
                text.Append(Pad(Ns(r.BuiltInEqualsNs), 16));
                text.Append(Pad(Ratio(r.GeneratedEqualsNs, r.BuiltInEqualsNs), 8));
                text.Append(Pad(Ns(r.GeneratedHashNs), 14));
                text.Append(Pad(Ns(r.BuiltInHashNs), 16));
                text.Append(Pad(Ratio(r.GeneratedHashNs, r.BuiltInHashNs), 8));
                text.Append(Pad(double.IsNaN(r.Generated64HashNs) ? "-" : Ns(r.Generated64HashNs), 14));
                text.AppendLine(double.IsNaN(r.Generated64HashNs) ? "-" : Ratio(r.Generated64HashNs, r.BuiltInHashNs));
            }

            return text.ToString();
        }

        private static string Pad(string value, int width)
        {
            return value.Length >= width ? value + " " : value.PadRight(width);
        }

        private static string Ns(double value)
        {
            return value.ToString("N1", CultureInfo.InvariantCulture) + " ns";
        }

        private static string Ratio(double generated, double builtIn)
        {
            return builtIn <= 0 ? "-" : (generated / builtIn).ToString("0.00", CultureInfo.InvariantCulture) + "x";
        }

        private static double Measure(Func<bool> action, int milliseconds)
        {
            Warm(action);
            var budget = Stopwatch.Frequency * milliseconds / 1000;
            long ops = 0;
            var sink = 0;
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedTicks < budget)
            {
                for (var i = 0; i < 64; i++)
                    sink += action() ? 1 : 0;

                ops += 64;
            }

            watch.Stop();
            Sink += sink;
            return watch.ElapsedTicks * (1e9 / Stopwatch.Frequency) / ops;
        }

        private static double Measure(Func<int> action, int milliseconds)
        {
            Warm(action);
            var budget = Stopwatch.Frequency * milliseconds / 1000;
            long ops = 0;
            var sink = 0;
            var watch = Stopwatch.StartNew();
            while (watch.ElapsedTicks < budget)
            {
                for (var i = 0; i < 64; i++)
                    sink += action();

                ops += 64;
            }

            watch.Stop();
            Sink += sink;
            return watch.ElapsedTicks * (1e9 / Stopwatch.Frequency) / ops;
        }

        /// <summary>A quiet period for background compilation. The browser's single thread cannot sleep, and does not need to.</summary>
        private static void Settle()
        {
            try
            {
                System.Threading.Thread.Sleep(300);
            }
            catch (PlatformNotSupportedException)
            {
            }
        }

        /// <summary>Runs for a short while first, so tiering and any lazy initialization are behind the measurement.</summary>
        private static void Warm(Func<bool> action)
        {
            var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 25;
            var sink = 0;
            while (Stopwatch.GetTimestamp() < until)
                sink += action() ? 1 : 0;

            Sink += sink;
        }

        private static void Warm(Func<int> action)
        {
            var until = Stopwatch.GetTimestamp() + Stopwatch.Frequency / 25;
            var sink = 0;
            while (Stopwatch.GetTimestamp() < until)
                sink += action();

            Sink += sink;
        }
    }
}
