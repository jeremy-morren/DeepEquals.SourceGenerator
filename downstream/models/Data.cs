// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

// Deterministic builders: the same seed gives a structurally equal value with no shared references, which is what
// both an equality assertion and an equality benchmark need. Written to C# 7.3.

// ReSharper disable All

using System;
using System.Collections.Generic;

namespace DeepEquals.Downstream
{
    public static class Data
    {
        public static GraphNode Graph(int seed)
        {
            var r = new Random(seed);
            var node = new GraphNode("secret " + seed)
            {
                Value = r.Next(),
                Tags = new List<string> { "x", "y", "z" + r.Next(10) },
                Counts = new Dictionary<string, int> { { "k", 1 }, { "j", 2 }, { "n", r.Next(100) } },
                Stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(r.Next(100000)),
                Amount = 1.10m + r.Next(1000),
                Rate = r.NextDouble(),
            };
            node.Next = node;   // a cycle: the comparer must terminate
            return node;
        }

        public static Customer Customer(int seed)
        {
            var r = new Random(seed);
            return new Customer
            {
                Name = "Customer " + r.Next(1000),
                Email = "customer" + r.Next(1000) + "@example.com",
                Age = r.Next(90),
                Id = NextInt64(r),
                Key = new Guid(seed, 0, 0, 1, 2, 3, 4, 5, 6, 7, 8),
                Created = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMinutes(r.Next(100000)),
                Balance = r.Next(1000000) / 100m,
                Score = r.NextDouble(),
            };
        }

        public static OrderLine Line(Random r, int position)
        {
            return new OrderLine
            {
                Sku = new SkuId(r.Next(10000)),
                Price = r.Next(100000) / 100m,
                Quantity = r.Next(1000) / 10m,
                Position = position,
                Note = "line " + position,
                When = new DateTimeOffset(2021, 3, 4, 5, 6, 7, TimeSpan.FromHours(2)).AddMinutes(r.Next(10000)),
            };
        }

        public static List<OrderLine> Lines(int seed, int count)
        {
            var r = new Random(seed);
            var lines = new List<OrderLine>(count);
            for (var i = 0; i < count; i++)
                lines.Add(Line(r, i));

            return lines;
        }

        public static Dictionary<string, decimal> StringMap(int seed, int count)
        {
            var r = new Random(seed);
            var map = new Dictionary<string, decimal>(count);
            for (var i = 0; i < count; i++)
                map["key" + i] = r.Next(100000) / 100m;

            return map;
        }

        public static Dictionary<SkuId, int> SkuMap(int seed, int count)
        {
            var r = new Random(seed);
            var map = new Dictionary<SkuId, int>(count);
            for (var i = 0; i < count; i++)
                map[new SkuId(i)] = r.Next();

            return map;
        }

        public static Order Order(int seed)
        {
            var r = new Random(seed);
            return new Order
            {
                Sku = new SkuId(r.Next(10000)),
                Price = r.Next(100000) / 100m,
                Quantity = r.Next(1000) / 10m,
                Reference = "order " + r.Next(1000),
                Lines = Lines(seed + 1, 20),
                Weights = new List<decimal> { 1.5m, 2.25m, 3.125m, 4m },
                Attributes = StringMap(seed + 2, 10),
                Counts = SkuMap(seed + 3, 10),
                Tags = new HashSet<string> { "a", "b", "c", "d" },
                Shape = new Circle { Name = "c", Radius = 1.5 },
            };
        }

        public static Square Square(int seed)
        {
            return new Square { Name = "square " + seed, Width = 3 + seed, Height = 4, Extra = 5 };
        }

        /// <summary>A tree of <c>1 + fanout + fanout² + …</c> nodes: 585 for depth 3 and fanout 8.</summary>
        public static TreeNode Tree(int seed, int depth, int fanout)
        {
            var r = new Random(seed);
            return BuildTree(r, depth, fanout);
        }

        private static TreeNode BuildTree(Random r, int level, int fanout)
        {
            var node = new TreeNode { Value = r.Next(), Children = new List<TreeNode>() };
            if (level > 0)
                for (var i = 0; i < fanout; i++)
                    node.Children.Add(BuildTree(r, level - 1, fanout));

            return node;
        }

        /// <summary>Only leaf values, so that <c>object.Equals</c> is a meaningful built-in to measure against.</summary>
        public static Payload Payload(int seed)
        {
            var r = new Random(seed);
            var values = new Dictionary<string, object>(8)
            {
                { "int", r.Next() },
                { "text", "text " + r.Next() },
                { "money", r.Next(100000) / 100m },
                { "when", new DateTime(2022, 2, 2, 0, 0, 0, DateTimeKind.Utc).AddMinutes(r.Next(1000)) },
                { "id", new SkuId(r.Next()) },
                { "ratio", r.NextDouble() },
                { "big", NextInt64(r) },
                { "key", new Guid(seed, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10) },
            };
            return new Payload { Values = values, Single = r.Next(100000) / 100m };
        }

#if RECORDS
        public static Money Money(int seed)
        {
            var r = new Random(seed);
            return new Money(r.Next(1000000) / 100m, "EUR");
        }

        public static Point3 Point3(int seed)
        {
            var r = new Random(seed);
            return new Point3(r.NextDouble(), r.NextDouble(), r.NextDouble());
        }

        public static Address Address(int seed)
        {
            var r = new Random(seed);
            return new Address("Street " + r.Next(100), "City " + r.Next(100), r.Next(100000));
        }

        public static Invoice Invoice(int seed)
        {
            var r = new Random(seed);
            return new Invoice(NextInt64(r), Address(seed + 1), Money(seed + 2), "notes " + r.Next(100));
        }

        public static Person Person(int seed)
        {
            var r = new Random(seed);
            return new Person("Person " + r.Next(100), Address(seed + 1), Point3(seed + 2), Money(seed + 3));
        }
#endif

        /// <summary>A linked list of <paramref name="length"/> nodes whose values count up from <paramref name="start"/>.</summary>
        public static ListNode Chain(int length, int start)
        {
            ListNode head = null;
            for (var i = length - 1; i >= 0; i--)
                head = new ListNode { Value = start + i, Next = head };

            return head;
        }

        /// <summary>A node whose Next is itself, and the same loop unrolled into two nodes.</summary>
        public static ListNode RolledLoop(int value)
        {
            var node = new ListNode { Value = value };
            node.Next = node;
            return node;
        }

        public static ListNode UnrolledLoop(int value)
        {
            var a = new ListNode { Value = value };
            var b = new ListNode { Value = value, Next = a };
            a.Next = b;
            return a;
        }

        /// <summary>
        /// The audit's collision case: 65 chains 0 -> 0 -> i -> null. The public hash sees two equal nodes in each, so
        /// only a fingerprint that looks two payload edges in tells them apart.
        /// </summary>
        public static HashSet<ListNode> ChainSet(int count)
        {
            var set = new HashSet<ListNode>();
            for (var i = 0; i < count; i++)
                set.Add(new ListNode { Value = 0, Next = new ListNode { Value = 0, Next = new ListNode { Value = i } } });

            return set;
        }

        /// <summary>
        /// A full tree whose every second-level subtree is one shared instance, <paramref name="sharedLevels"/> deep:
        /// Graph compares a shared subtree once, Path and Tree once per path that reaches it.
        /// </summary>
        public static TreeNode Diamond(int seed, int sharedLevels, int subtreeDepth)
        {
            var shared = Tree(seed, subtreeDepth, 2);
            var node = shared;
            for (var i = 0; i < sharedLevels; i++)
                node = new TreeNode { Value = seed + i, Children = new List<TreeNode> { node, node } };

            return node;
        }

        /// <summary>
        /// <paramref name="count"/> keys of which <paramref name="colliding"/> share one group, and so one fingerprint.
        /// With <paramref name="mismatchLast"/> the last colliding key differs, so the collision run decides late.
        /// </summary>
        public static HashSet<CollidingId> CollidingSet(int count, int colliding, bool mismatchLast)
        {
            var set = new HashSet<CollidingId>();
            for (var i = 0; i < count; i++)
            {
                var group = i < colliding ? -1 : i;
                var id = mismatchLast && i == colliding - 1 ? -i - 1 : i;
                set.Add(new CollidingId(group, id));
            }

            return set;
        }

        public static Texts Texts(int seed, int length)
        {
            var r = new Random(seed);
            Func<string> next = () =>
            {
                var chars = new char[length];
                for (var i = 0; i < length; i++) chars[i] = (char)('a' + r.Next(26));
                return new string(chars);
            };

            return new Texts { A = next(), B = next(), C = next(), D = next(), E = next(), F = next(), G = next(), H = next() };
        }

        public static double[] Doubles(int seed, int count)
        {
            var r = new Random(seed);
            var values = new double[count];
            for (var i = 0; i < count; i++) values[i] = r.NextDouble() * 1000 + 1;
            return values;
        }

        public static Guid[] Guids(int seed, int count)
        {
            var values = new Guid[count];
            for (var i = 0; i < count; i++) values[i] = new Guid(seed, (short)i, (short)(i >> 16), 1, 2, 3, 4, 5, 6, 7, 8);
            return values;
        }

        public static decimal[] Decimals(int seed, int count)
        {
            var r = new Random(seed);
            var values = new decimal[count];
            for (var i = 0; i < count; i++) values[i] = r.Next(1000000) / 100m;
            return values;
        }

        public static Arrays Arrays(int seed, int count)
        {
            return new Arrays
            {
                Doubles = Doubles(seed, count),
                Guids = Guids(seed, count),
                Decimals = Decimals(seed, count),
                DoubleView = new ReadOnlyListView<double>(Doubles(seed + 1, count)),
            };
        }

#if RECORDS
        public static Point3[] Point3s(int seed, int count)
        {
            var r = new Random(seed);
            var values = new Point3[count];
            for (var i = 0; i < count; i++) values[i] = new Point3(r.NextDouble() + 1, r.NextDouble() + 1, r.NextDouble() + 1);
            return values;
        }
#endif

        private static long NextInt64(Random r)
        {
            return ((long)r.Next() << 32) | (uint)r.Next();
        }
    }
}
