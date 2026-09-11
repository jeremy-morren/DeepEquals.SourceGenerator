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
    }

    public static class MicroBench
    {
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
                });
            }

            return results;
        }

        /// <summary>A fixed-width table, one scenario per line, with the generated-over-built-in ratios.</summary>
        public static string Format(List<MicroResult> results)
        {
            var text = new StringBuilder();
            text.AppendLine(Pad("scenario", 48) + Pad("equals gen", 14) + Pad("equals builtin", 16) + Pad("ratio", 8) + Pad("hash gen", 14) + Pad("hash builtin", 16) + "ratio");
            foreach (var r in results)
            {
                text.Append(Pad(r.Name, 48));
                text.Append(Pad(Ns(r.GeneratedEqualsNs), 14));
                text.Append(Pad(Ns(r.BuiltInEqualsNs), 16));
                text.Append(Pad(Ratio(r.GeneratedEqualsNs, r.BuiltInEqualsNs), 8));
                text.Append(Pad(Ns(r.GeneratedHashNs), 14));
                text.Append(Pad(Ns(r.BuiltInHashNs), 16));
                text.AppendLine(Ratio(r.GeneratedHashNs, r.BuiltInHashNs));
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
