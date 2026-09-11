// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System.Collections.Generic;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using DeepEquals.Downstream;

namespace DeepEquals.Benchmarks
{
    /// <summary>In process, so every target framework this project builds for can host its own run.</summary>
    public sealed class InProcessConfig : ManualConfig
    {
        public InProcessConfig()
        {
            AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance));
            AddLogicalGroupRules(BenchmarkLogicalGroupRule.ByCategory, BenchmarkLogicalGroupRule.ByParams);
        }
    }

    /// <summary>
    /// The generated comparer against the built-in or hand-written equality, for every shared scenario. Both sides go
    /// through the same delegate, so the small call overhead cancels in the ratio. The ratio column reads generated
    /// over built-in: below 1 the generated comparer is faster. The hash category times both hash widths.
    /// </summary>
    [Config(typeof(InProcessConfig))]
    [MemoryDiagnoser]
    [CategoriesColumn]
    [GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory, BenchmarkLogicalGroupRule.ByParams)]
    public class EqualityBenchmarks
    {
        public static IEnumerable<Scenario> Cases() => Scenarios.All();

        [Benchmark(Baseline = true), BenchmarkCategory("Equals"), ArgumentsSource(nameof(Cases))]
        public bool BuiltIn_Equals(Scenario scenario) => scenario.BuiltInEquals();

        [Benchmark, BenchmarkCategory("Equals"), ArgumentsSource(nameof(Cases))]
        public bool Generated_Equals(Scenario scenario) => scenario.GeneratedEquals();

        [Benchmark(Baseline = true), BenchmarkCategory("GetHashCode"), ArgumentsSource(nameof(Cases))]
        public int BuiltIn_GetHashCode(Scenario scenario) => scenario.BuiltInHash();

        [Benchmark, BenchmarkCategory("GetHashCode"), ArgumentsSource(nameof(Cases))]
        public int Generated_GetHashCode(Scenario scenario) => scenario.GeneratedHash();

        /// <summary>The 64-bit stream over the same value; the Path and Tree scenarios have none and report the 32-bit one.</summary>
        [Benchmark, BenchmarkCategory("GetHashCode"), ArgumentsSource(nameof(Cases))]
        public int Generated64_GetHashCode(Scenario scenario) => scenario.Generated64Hash != null ? scenario.Generated64Hash() : scenario.GeneratedHash();
    }
}
