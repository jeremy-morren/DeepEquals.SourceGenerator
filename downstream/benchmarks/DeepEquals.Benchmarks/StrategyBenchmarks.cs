// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

// The benchmarks of the update plan's section 6: each class names what it is meant to expose. They share the in-process
// configuration, so every target framework hosts its own run, and the downstream models, so the data is the data the
// smoke checks assert on.

using System;
using System.Collections.Generic;
using System.Linq;
using BenchmarkDotNet.Attributes;
using DeepEquals.Downstream;

namespace DeepEquals.Benchmarks
{
    /// <summary>The comparer for a mode: Graph, the default; Path; or Tree.</summary>
    internal static class Modes
    {
        public static IEqualityComparer<TreeNode> Tree(string mode) =>
            mode == "Path" ? DownstreamPathContext.TreeNode : mode == "Tree" ? (IEqualityComparer<TreeNode>)DownstreamTreeContext.TreeNode : DownstreamContext.TreeNode;

        public static IEqualityComparer<ListNode> Chain(string mode) =>
            mode == "Path" ? DownstreamPathContext.ListNode : mode == "Tree" ? (IEqualityComparer<ListNode>)DownstreamTreeContext.ListNode : DownstreamContext.ListNode;

        public static IEqualityComparer<HashSet<TreeNode>> TreeSet(string mode) =>
            mode == "Path" ? DownstreamPathContext.HashSetOfTreeNode : mode == "Tree" ? (IEqualityComparer<HashSet<TreeNode>>)DownstreamTreeContext.HashSetOfTreeNode : DownstreamContext.HashSetOfTreeNode;
    }

    /// <summary>The guard cost against the depth of nesting rather than node count: 585 nodes four deep, 1,023 ten deep.</summary>
    [Config(typeof(InProcessConfig))]
    [MemoryDiagnoser]
    public class CycleHandlingBenchmarks
    {
        private IEqualityComparer<TreeNode> _comparer;
        private TreeNode _a;
        private TreeNode _b;

        [Params("Graph", "Path", "Tree")]
        public string Mode { get; set; }

        [Params("8x3 (585)", "2x9 (1023)")]
        public string Shape { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _comparer = Modes.Tree(Mode);
            var (fanout, depth) = Shape.StartsWith("8", StringComparison.Ordinal) ? (8, 3) : (2, 9);
            _a = Data.Tree(1, depth, fanout);
            _b = Data.Tree(1, depth, fanout);
        }

        [Benchmark]
        public bool Compare() => _comparer.Equals(_a, _b);

        [Benchmark]
        public int Hash() => _comparer.GetHashCode(_a);
    }

    /// <summary>The tail loop, Brent's guard, and the Path state growing with the chain.</summary>
    [Config(typeof(InProcessConfig))]
    [MemoryDiagnoser]
    public class ChainBenchmarks
    {
        private IEqualityComparer<ListNode> _comparer;
        private ListNode _a;
        private ListNode _b;

        [Params("Graph", "Path", "Tree")]
        public string Mode { get; set; }

        [Params(10, 1000, 100000)]
        public int Length { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _comparer = Modes.Chain(Mode);
            _a = Data.Chain(Length, 0);
            _b = Data.Chain(Length, 0);
        }

        [Benchmark]
        public bool Compare() => _comparer.Equals(_a, _b);

        /// <summary>Graph and Path hash one node in; Tree walks the whole chain.</summary>
        [Benchmark]
        public int Hash() => _comparer.GetHashCode(_a);
    }

    /// <summary>
    /// A shared subtree: Graph compares it once through its memo; Path and Tree once per path. One level of sharing over
    /// a 255-node subtree, then eight levels over a 15-node one, the exponential case.
    /// </summary>
    [Config(typeof(InProcessConfig))]
    [MemoryDiagnoser]
    public class DiamondBenchmarks
    {
        private IEqualityComparer<TreeNode> _comparer;
        private TreeNode _a;
        private TreeNode _b;

        [Params("Graph", "Path", "Tree")]
        public string Mode { get; set; }

        [Params(1, 8)]
        public int SharedLevels { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _comparer = Modes.Tree(Mode);
            var subtreeDepth = SharedLevels == 1 ? 7 : 3;
            _a = Data.Diamond(1, SharedLevels, subtreeDepth);
            _b = Data.Diamond(1, SharedLevels, subtreeDepth);
        }

        [Benchmark]
        public bool Compare() => _comparer.Equals(_a, _b);
    }

    /// <summary>
    /// The inline-to-spill boundary: a star of 8 nodes retains 8 pairs, one of 9 spills. A line of 64 nodes makes the Path
    /// state spill and grow, then roll back on every return.
    /// </summary>
    [Config(typeof(InProcessConfig))]
    [MemoryDiagnoser]
    public class RetainedPairsBenchmarks
    {
        private IEqualityComparer<TreeNode> _comparer;
        private TreeNode _a;
        private TreeNode _b;

        [Params("Graph", "Path")]
        public string Mode { get; set; }

        [Params("star 8", "star 9", "line 64")]
        public string Shape { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _comparer = Modes.Tree(Mode);
            _a = Build();
            _b = Build();
        }

        private TreeNode Build()
        {
            if (Shape.StartsWith("line", StringComparison.Ordinal))
            {
                var node = new TreeNode { Value = 0, Children = new List<TreeNode>() };
                for (var i = 1; i < 64; i++)
                    node = new TreeNode { Value = i, Children = new List<TreeNode> { node } };

                return node;
            }

            var leaves = Shape == "star 8" ? 7 : 8;
            return new TreeNode { Value = -1, Children = Enumerable.Range(0, leaves).Select(i => new TreeNode { Value = i, Children = new List<TreeNode>() }).ToList() };
        }

        [Benchmark]
        public bool Compare() => _comparer.Equals(_a, _b);
    }

    /// <summary>A hundred recursive-typed elements in a set: stateful trials with mark and rollback, against plain recursion under Tree.</summary>
    [Config(typeof(InProcessConfig))]
    [MemoryDiagnoser]
    public class CyclicSetBenchmarks
    {
        private IEqualityComparer<HashSet<TreeNode>> _comparer;
        private HashSet<TreeNode> _a;
        private HashSet<TreeNode> _b;

        [Params("Graph", "Path", "Tree")]
        public string Mode { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _comparer = Modes.TreeSet(Mode);
            _a = new HashSet<TreeNode>(Enumerable.Range(0, 100).Select(i => Data.Tree(i, 2, 3)));
            _b = new HashSet<TreeNode>(Enumerable.Range(0, 100).Reverse().Select(i => Data.Tree(i, 2, 3)));
        }

        [Benchmark]
        public bool Compare() => _comparer.Equals(_a, _b);

        [Benchmark]
        public int Hash() => _comparer.GetHashCode(_a);
    }

    /// <summary>
    /// The audit's 65 chains 0 -> 0 -> i: at fingerprint depth 1 all share one fingerprint and one exact-matching run of 65;
    /// at depth 2 and the default 4 each is its own bucket. The fingerprint's cost against the matching it removes.
    /// Depth 1 at the default cap of 64 throws, and is asserted by the smoke checks rather than timed.
    /// </summary>
    [Config(typeof(InProcessConfig))]
    [MemoryDiagnoser]
    public class FingerprintBenchmarks
    {
        private IEqualityComparer<HashSet<ListNode>> _comparer;
        private HashSet<ListNode> _a;
        private HashSet<ListNode> _b;

        [Params(1, 2, 4)]
        public int Depth { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _comparer = Depth == 1 ? DownstreamMatch1WideContext.HashSetOfListNode
                : Depth == 2 ? (IEqualityComparer<HashSet<ListNode>>)DownstreamMatch2Context.HashSetOfListNode
                : DownstreamContext.HashSetOfListNode;
            _a = Data.ChainSet(65);
            _b = Data.ChainSet(65);
        }

        [Benchmark]
        public bool Compare() => _comparer.Equals(_a, _b);

        [Benchmark]
        public int Hash() => _comparer.GetHashCode(_a);
    }

    /// <summary>
    /// The k-by-k matching inside a collision run, which the audit's O3 targets: a hundred keys of which 0, 10, 63, 64 or
    /// 100 share one fingerprint, equal or with the last colliding key different. Allocations show the matching workspace.
    /// </summary>
    [Config(typeof(InProcessConfig))]
    [MemoryDiagnoser]
    public class CollisionRunBenchmarks
    {
        private HashSet<CollidingId> _a;
        private HashSet<CollidingId> _b;

        [Params(0, 10, 63, 64, 100)]
        public int Colliding { get; set; }

        [Params(false, true)]
        public bool MismatchLast { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _a = Data.CollidingSet(100, Colliding, false);
            _b = Data.CollidingSet(100, Colliding, MismatchLast && Colliding > 0);
        }

        [Benchmark]
        public bool Compare() => DownstreamMatch1WideContext.HashSetOfCollidingId.Equals(_a, _b);
    }

    /// <summary>Hash quality and cost together: a thousand adds and lookups in a hash table built on each comparer.</summary>
    [Config(typeof(InProcessConfig))]
    [MemoryDiagnoser]
    public class HashTableBenchmarks
    {
        private Customer[] _customers;
        private Customer[] _probes;
        private Order[] _orders;
        private Order[] _orderProbes;
        private IEqualityComparer<Customer> _customerComparer;
        private IEqualityComparer<Order> _orderComparer;

        [Params("built-in", "XxHash32", "XxHash64")]
        public string Comparer { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _customers = Enumerable.Range(0, 1000).Select(Data.Customer).ToArray();
            _probes = Enumerable.Range(0, 1000).Select(Data.Customer).ToArray();
            _orders = Enumerable.Range(0, 200).Select(Data.Order).ToArray();
            _orderProbes = Enumerable.Range(0, 200).Select(Data.Order).ToArray();
            _customerComparer = Comparer == "XxHash32" ? DownstreamContext.Customer
                : Comparer == "XxHash64" ? (IEqualityComparer<Customer>)DownstreamHash64Context.Customer
                : new BuiltIn.Comparer<Customer>(BuiltIn.CustomerEquals, BuiltIn.CustomerHash);
            _orderComparer = Comparer == "XxHash32" ? DownstreamContext.Order
                : Comparer == "XxHash64" ? (IEqualityComparer<Order>)DownstreamHash64Context.Order
                : new BuiltIn.Comparer<Order>(BuiltIn.OrderEquals, BuiltIn.OrderHash);
        }

        [Benchmark]
        public int HashSetOfCustomer()
        {
            var set = new HashSet<Customer>(_customerComparer);
            foreach (var c in _customers) set.Add(c);
            var hits = 0;
            foreach (var p in _probes) if (set.Contains(p)) hits++;
            return hits;
        }

        [Benchmark]
        public int DictionaryOfOrder()
        {
            var map = new Dictionary<Order, int>(_orderComparer);
            for (var i = 0; i < _orders.Length; i++) map[_orders[i]] = i;
            var hits = 0;
            foreach (var p in _orderProbes) if (map.ContainsKey(p)) hits++;
            return hits;
        }
    }

    /// <summary>How much the string hash dominates a string-heavy record: eight strings of 8, 64 and 1,024 characters.</summary>
    [Config(typeof(InProcessConfig))]
    [MemoryDiagnoser]
    public class StringRecordBenchmarks
    {
        private Texts _a;
        private Texts _b;

        [Params(8, 64, 1024)]
        public int Length { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _a = Data.Texts(1, Length);
            _b = Data.Texts(1, Length);
        }

        [Benchmark(Baseline = true)]
        public bool BuiltIn_Equals() => BuiltIn.TextsEquals(_a, _b);

        [Benchmark]
        public bool Generated_Equals() => DownstreamContext.Texts.Equals(_a, _b);

        [Benchmark]
        public int BuiltIn_GetHashCode() => BuiltIn.TextsHash(_a);

        [Benchmark]
        public int Generated_GetHashCode() => DownstreamContext.Texts.GetHashCode(_a);

        [Benchmark]
        public int Generated64_GetHashCode() => DownstreamHash64Context.Texts.GetHashCode(_a);
    }

    /// <summary>The two-word leaves on both widths and the bit-block paths: a thousand Guids and a thousand decimals.</summary>
    [Config(typeof(InProcessConfig))]
    [MemoryDiagnoser]
    public class WideArrayBenchmarks
    {
        private Guid[] _guidsA;
        private Guid[] _guidsB;
        private decimal[] _decimalsA;
        private decimal[] _decimalsB;

        [GlobalSetup]
        public void Setup()
        {
            _guidsA = Data.Guids(1, 1000);
            _guidsB = Data.Guids(1, 1000);
            _decimalsA = Data.Decimals(1, 1000);
            _decimalsB = Data.Decimals(1, 1000);
        }

        [Benchmark]
        public bool BuiltIn_Guids_Equals() => BuiltIn.SequenceEquals(_guidsA, _guidsB);

        [Benchmark]
        public bool Generated_Guids_Equals() => DownstreamContext.ArrayOfGuid.Equals(_guidsA, _guidsB);

        [Benchmark]
        public int BuiltIn_Guids_GetHashCode() => BuiltIn.SequenceHash(_guidsA);

        [Benchmark]
        public int Generated_Guids_GetHashCode() => DownstreamContext.ArrayOfGuid.GetHashCode(_guidsA);

        [Benchmark]
        public int Generated64_Guids_GetHashCode() => DownstreamHash64Context.ArrayOfGuid.GetHashCode(_guidsA);

        [Benchmark]
        public bool BuiltIn_Decimals_Equals() => BuiltIn.SequenceEquals(_decimalsA, _decimalsB);

        [Benchmark]
        public bool Generated_Decimals_Equals() => DownstreamContext.ArrayOfDecimal.Equals(_decimalsA, _decimalsB);

        [Benchmark]
        public int BuiltIn_Decimals_GetHashCode() => BuiltIn.SequenceHash(_decimalsA);

        [Benchmark]
        public int Generated_Decimals_GetHashCode() => DownstreamContext.ArrayOfDecimal.GetHashCode(_decimalsA);

        [Benchmark]
        public int Generated64_Decimals_GetHashCode() => DownstreamHash64Context.ArrayOfDecimal.GetHashCode(_decimalsA);
    }

    /// <summary>
    /// Bit-block sequences at three lengths, equal and with a mismatch in the last element: the memory compare and XxHash3
    /// against an element loop, and the same values behind a list that is not an array, which takes the copy path.
    /// </summary>
    [Config(typeof(InProcessConfig))]
    [MemoryDiagnoser]
    public class BitBlockBenchmarks
    {
        private double[] _a;
        private double[] _b;
        private IReadOnlyList<double> _view;
#if RECORDS
        private Point3[] _pa;
        private Point3[] _pb;
        private IReadOnlyList<Point3> _pView;
#endif

        [Params(16, 256, 4096)]
        public int Length { get; set; }

        [Params(false, true)]
        public bool MismatchLast { get; set; }

        [GlobalSetup]
        public void Setup()
        {
            _a = Data.Doubles(1, Length);
            _b = Data.Doubles(1, Length);
            _view = new ReadOnlyListView<double>(Data.Doubles(1, Length));
            if (MismatchLast) _b[Length - 1] += 1;
#if RECORDS
            _pa = Data.Point3s(1, Length);
            _pb = Data.Point3s(1, Length);
            _pView = new ReadOnlyListView<Point3>(Data.Point3s(1, Length));
            if (MismatchLast) _pb[Length - 1] = _pb[Length - 1] with { X = -1 };
#endif
        }

        [Benchmark]
        public bool BuiltIn_Doubles_Equals() => BuiltIn.SequenceEquals(_a, _b);

        [Benchmark]
        public bool Generated_Doubles_Equals() => DownstreamContext.ArrayOfDouble.Equals(_a, _b);

        [Benchmark]
        public int BuiltIn_Doubles_GetHashCode() => BuiltIn.SequenceHash(_a);

        [Benchmark]
        public int Generated_Doubles_GetHashCode() => DownstreamContext.ArrayOfDouble.GetHashCode(_a);

        [Benchmark]
        public int Generated64_Doubles_GetHashCode() => DownstreamHash64Context.ArrayOfDouble.GetHashCode(_a);

        /// <summary>A list that is not an array: the elements are copied into a pooled buffer and hashed as bytes.</summary>
        [Benchmark]
        public int Generated_DoubleView_GetHashCode() => DownstreamContext.IReadOnlyListOfDouble.GetHashCode(_view);

#if RECORDS
        [Benchmark]
        public bool BuiltIn_Points_Equals() => BuiltIn.SequenceEquals(_pa, _pb);

        [Benchmark]
        public bool Generated_Points_Equals() => DownstreamContext.ArrayOfPoint3.Equals(_pa, _pb);

        [Benchmark]
        public int BuiltIn_Points_GetHashCode() => BuiltIn.SequenceHash(_pa);

        [Benchmark]
        public int Generated_Points_GetHashCode() => DownstreamContext.ArrayOfPoint3.GetHashCode(_pa);

        [Benchmark]
        public int Generated_PointView_GetHashCode() => DownstreamContext.IReadOnlyListOfPoint3.GetHashCode(_pView);
#endif
    }

    /// <summary>Early exit and the order members are compared in: the first declared member unequal, then the last.</summary>
    [Config(typeof(InProcessConfig))]
    [MemoryDiagnoser]
    public class MismatchBenchmarks
    {
        private Customer _customer;
        private Customer _customerFirst;
        private Customer _customerLast;
        private Order _order;
        private Order _orderFirst;
        private Order _orderLast;

        [GlobalSetup]
        public void Setup()
        {
            _customer = Data.Customer(1);
            _customerFirst = Data.Customer(1);
            _customerFirst.Name = "other";
            _customerLast = Data.Customer(1);
            _customerLast.Score += 1;
            _order = Data.Order(2);
            _orderFirst = Data.Order(2);
            _orderFirst.Sku = new SkuId(-1);
            _orderLast = Data.Order(2);
            _orderLast.Shape = new Circle { Name = "c", Radius = 2.5 };
        }

        [Benchmark]
        public bool Customer_FirstMemberDiffers() => DownstreamContext.Customer.Equals(_customer, _customerFirst);

        [Benchmark]
        public bool Customer_LastMemberDiffers() => DownstreamContext.Customer.Equals(_customer, _customerLast);

        [Benchmark]
        public bool BuiltIn_Customer_LastMemberDiffers() => BuiltIn.CustomerEquals(_customer, _customerLast);

        [Benchmark]
        public bool Order_FirstMemberDiffers() => DownstreamContext.Order.Equals(_order, _orderFirst);

        [Benchmark]
        public bool Order_LastMemberDiffers() => DownstreamContext.Order.Equals(_order, _orderLast);

        [Benchmark]
        public bool BuiltIn_Order_LastMemberDiffers() => BuiltIn.OrderEquals(_order, _orderLast);
    }
}
