// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

// The model types every downstream project shares: the smoke consumers assert on them, the benchmarks time them.
// Written to C# 7.3, because the lowest consumer tier compiles them at that language version. Records live in
// Records.cs, which is only compiled where the language allows them.

// ReSharper disable All

using System;
using System.Collections.Generic;

namespace DeepEquals.Downstream
{
    /// <summary>
    /// A type shaped to reach the interesting parts of a generated comparer from one object: a private
    /// field, a reference cycle, an ordered collection, an unordered dictionary, and three leaves whose
    /// exact representation matters (DateTime kind, decimal scale, negative zero).
    /// </summary>
    public sealed class GraphNode
    {
        private string _secret;

        public GraphNode(string secret) { _secret = secret; }

        public int Value { get; set; }
        public GraphNode Next { get; set; }
        public List<string> Tags { get; set; }
        public Dictionary<string, int> Counts { get; set; }
        public DateTime Stamp { get; set; }
        public decimal Amount { get; set; }
        public double Rate { get; set; }
    }

    /// <summary>A sealed class of wide leaves: the shape whose hash is one stream of words.</summary>
    public sealed class Customer
    {
        public string Name { get; set; }
        public string Email { get; set; }
        public int Age { get; set; }
        public long Id { get; set; }
        public Guid Key { get; set; }
        public DateTime Created { get; set; }
        public decimal Balance { get; set; }
        public double Score { get; set; }
    }

    /// <summary>A strongly typed id, registered as a simple type, so it is a dictionary key the default comparer agrees on.</summary>
    public readonly struct SkuId : IEquatable<SkuId>
    {
        public SkuId(int value) { Value = value; }

        public int Value { get; }

        public bool Equals(SkuId other) { return Value == other.Value; }

        public override bool Equals(object obj) { return obj is SkuId && Equals((SkuId)obj); }

        public override int GetHashCode() { return Value; }
    }

    public abstract class Shape
    {
        public string Name { get; set; }
    }

    public sealed class Circle : Shape
    {
        public double Radius { get; set; }
    }

    public class Rect : Shape
    {
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public sealed class Square : Rect
    {
        public int Extra { get; set; }
    }

    public sealed class OrderLine
    {
        public SkuId Sku { get; set; }
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
        public int Position { get; set; }
        public string Note { get; set; }
        public DateTimeOffset When { get; set; }
    }

    /// <summary>Unsealed on purpose: most contract classes are, and each one dispatches on its runtime type.</summary>
    public class Order
    {
        public SkuId Sku { get; set; }
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
        public string Reference { get; set; }
        public IReadOnlyList<OrderLine> Lines { get; set; }
        public IReadOnlyList<decimal> Weights { get; set; }
        public Dictionary<string, decimal> Attributes { get; set; }
        public Dictionary<SkuId, int> Counts { get; set; }
        public HashSet<string> Tags { get; set; }
        public Shape Shape { get; set; }
    }

    /// <summary>A cyclic type that holds a tree at runtime: every pair enters the comparison state, none is ever a cycle.</summary>
    public sealed class TreeNode
    {
        public int Value { get; set; }
        public List<TreeNode> Children { get; set; }
    }

    /// <summary>Values behind <c>object</c>: every entry dispatches on its runtime type.</summary>
    public sealed class Payload
    {
        public Dictionary<string, object> Values { get; set; }
        public object Single { get; set; }
    }

    /// <summary>A linked list: the chain shape, compared in a loop, whose public hash looks one node in.</summary>
    public sealed class ListNode
    {
        public int Value { get; set; }
        public ListNode Next { get; set; }
    }

    /// <summary>Eight strings: the record whose cost is the string hash itself, so gains elsewhere read against it.</summary>
    public sealed class Texts
    {
        public string A { get; set; }
        public string B { get; set; }
        public string C { get; set; }
        public string D { get; set; }
        public string E { get; set; }
        public string F { get; set; }
        public string G { get; set; }
        public string H { get; set; }
    }

    /// <summary>
    /// A key whose hash is only its group, registered as a simple type: every key of a group shares a fingerprint, so a
    /// set of them exercises the exact matching inside a collision run.
    /// </summary>
    public readonly struct CollidingId : IEquatable<CollidingId>
    {
        public CollidingId(int group, int id) { Group = group; Id = id; }

        public int Group { get; }

        public int Id { get; }

        public bool Equals(CollidingId other) { return Group == other.Group && Id == other.Id; }

        public override bool Equals(object obj) { return obj is CollidingId && Equals((CollidingId)obj); }

        public override int GetHashCode() { return Group; }
    }

    /// <summary>A read-only list that is neither an array nor a List, so a sequence behind it takes the copy path.</summary>
    public sealed class ReadOnlyListView<T> : IReadOnlyList<T>
    {
        private readonly T[] _items;

        public ReadOnlyListView(T[] items) { _items = items; }

        public T this[int index] { get { return _items[index]; } }

        public int Count { get { return _items.Length; } }

        public IEnumerator<T> GetEnumerator() { return ((IEnumerable<T>)_items).GetEnumerator(); }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() { return GetEnumerator(); }
    }

    /// <summary>The bit-block and wide-leaf sequences, each behind the declared type a contract would use.</summary>
    public sealed class Arrays
    {
        public double[] Doubles { get; set; }
        public Guid[] Guids { get; set; }
        public decimal[] Decimals { get; set; }
        public IReadOnlyList<double> DoubleView { get; set; }
    }
}
