// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using DeepEquals.SourceGeneration;

namespace DeepEquals.Smoke
{
    /// <summary>
    /// A type shaped to reach the interesting parts of a generated comparer from one object: a private
    /// field, a reference cycle, an ordered collection, an unordered dictionary, and three leaves whose
    /// exact representation matters (DateTime kind, decimal scale, negative zero).
    /// </summary>
    public sealed class Node
    {
        private string _secret;

        public Node(string secret) { _secret = secret; }

        public int Value { get; set; }
        public Node Next { get; set; }
        public List<string> Tags { get; set; }
        public Dictionary<string, int> Counts { get; set; }
        public DateTime Stamp { get; set; }
        public decimal Amount { get; set; }
        public double Rate { get; set; }
    }

    [GenerateDeepEquals(typeof(Node))]
    public partial class SmokeContext : DeepEqualsContextBase { }

    /// <summary>
    /// The assertions every consumer runs. Returning a string rather than throwing keeps this usable
    /// from a console Main, from a Blazor component and from anywhere else a consumer can be built.
    /// Written to C# 7.3, because the lowest consumer tier compiles it at that language version.
    /// </summary>
    public static class Checks
    {
        private static Node Sample()
        {
            return new Node("s")
            {
                Value = 1,
                Tags = new List<string> { "x", "y" },
                Counts = new Dictionary<string, int> { { "k", 1 }, { "j", 2 } },
                Stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                Amount = 1.10m,
                Rate = 0.5d
            };
        }

        /// <summary>Runs every assertion. Returns a line starting "OK" or one starting "FAIL".</summary>
        public static string Run()
        {
            List<string> failures = new List<string>();

            Node a = Sample();
            Node b = Sample();
            a.Next = a;
            b.Next = b;

            // A cycle must terminate, and two structurally identical graphs must agree.
            if (!SmokeContext.Node.Equals(a, b)) { failures.Add("equal"); }
            if (SmokeContext.Node.GetHashCode(a) != SmokeContext.Node.GetHashCode(b)) { failures.Add("hash"); }

            // Instance storage is compared, so a difference in a private field is visible.
            if (SmokeContext.Node.Equals(a, new Node("other"))) { failures.Add("private field"); }

            // Ordered collections compare element by element, strings by their UTF-16 code units.
            Node longer = Sample();
            longer.Tags = new List<string> { "x", "yy" };
            if (SmokeContext.Node.Equals(a, longer)) { failures.Add("ordered collection"); }

            Node otherKind = Sample();
            otherKind.Stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Local);
            if (SmokeContext.Node.Equals(a, otherKind)) { failures.Add("DateTime kind"); }

            Node otherScale = Sample();
            otherScale.Amount = 1.1m;
            if (SmokeContext.Node.Equals(a, otherScale)) { failures.Add("decimal scale"); }

            Node negativeZero = Sample();
            negativeZero.Rate = -0d;
            Node positiveZero = Sample();
            positiveZero.Rate = 0d;
            if (SmokeContext.Node.Equals(negativeZero, positiveZero)) { failures.Add("negative zero"); }

            // Dictionaries are sets of pairs; enumeration order must not reach the result.
            Node reordered = Sample();
            reordered.Counts = new Dictionary<string, int> { { "j", 2 }, { "k", 1 } };
            reordered.Next = reordered;
            if (!SmokeContext.Node.Equals(a, reordered)) { failures.Add("unordered dictionary"); }
            if (SmokeContext.Node.GetHashCode(a) != SmokeContext.Node.GetHashCode(reordered)) { failures.Add("unordered dictionary hash"); }

            if (failures.Count > 0)
            {
                return "FAIL " + string.Join(", ", failures.ToArray());
            }

            return "OK " + Describe();
        }

        /// <summary>A short note on the runtime, so a passing log says which one it passed on.</summary>
        public static string Describe()
        {
            string runtime = Type.GetType("Mono.Runtime") != null ? "mono" : "coreclr-or-netfx";
            return "runtime=" + runtime + " pointer=" + (IntPtr.Size * 8) + "-bit";
        }
    }
}
