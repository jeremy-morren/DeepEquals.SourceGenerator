// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

// ReSharper disable All

// Fixture models, written in C# 7.3 so the lowest tier compiles them verbatim.

using System;
using System.Collections.Generic;
using DeepEquals.SourceGeneration;

namespace DeepEquals.Fixtures
{
    public sealed class Person
    {
        private string _secret;

        public Person(string secret)
        {
            _secret = secret;
        }

        public string Name { get; set; }

        public int Age;

        public double Score { get; set; }

        public Guid Id { get; set; }

        public DateTime Born { get; set; }

        public decimal Balance;

        public long Ticks;

        public string Secret
        {
            get { return _secret; }
        }

        public int NotStored
        {
            get { throw new InvalidOperationException("computed properties are never read"); }
        }
    }

    public sealed class Node
    {
        public int Value;

        public Node Next { get; set; }

        public List<Node> Children { get; set; }
    }

    public abstract class Shape
    {
        public string Name { get; set; }
    }

    public sealed class Circle : Shape
    {
        public double Radius { get; set; }
    }

    public class Square : Shape
    {
        public int Side { get; set; }
    }

    public sealed class Cube : Square
    {
        public int Depth { get; set; }
    }

    public sealed class Unregistered : Shape
    {
    }

    public struct Point
    {
        public int X;
        public int Y;
    }

    public struct Big
    {
        public long A;
        public long B;
        public string Tag;
    }

    public enum Colour : byte
    {
        Red = 1,
        Green = 200,
    }

    public enum Wide : long
    {
        Low = 1L,
        High = 1L << 40,
    }

    public sealed class Holder
    {
        public Point P;

        public Point? Maybe;

        public Big Large;

        public Colour Colour;

        public Wide Wide;

        public HashSet<string> Tags;

        public Dictionary<string, int> Counts;

        public int[] Values;

        public IReadOnlyList<Point> Points;

        public IEnumerable<string> Words;

        public ISet<Node> Nodes;

        public object Anything;

        public Shape Shape;

        public KeyValuePair<int, Point> Pair;

        public ValueTuple<int, string> Tuple;

        public Tuple<int, Point> RefTuple;

        [DeepEqualsIgnore]
        public int Ignored;
    }

    public sealed class Empty
    {
    }

    public sealed class Box<T>
    {
        private T _value;

        public Box(T value)
        {
            _value = value;
        }
    }

    public sealed class Boxes
    {
        public Box<int> Int;

        public Box<string> Text;
    }

    [GenerateDeepEquals(typeof(Person))]
    [GenerateDeepEquals(typeof(Node))]
    [GenerateDeepEquals(typeof(Circle))]
    [GenerateDeepEquals(typeof(Cube))]
    [GenerateDeepEquals(typeof(Holder))]
    [GenerateDeepEquals(typeof(Empty))]
    [GenerateDeepEquals(typeof(Boxes))]
    public partial class FixtureContext : DeepEqualsContextBase
    {
    }

    /// <summary>The same closure under the 64-bit hash stream.</summary>
    [GenerateDeepEquals(typeof(Person))]
    [GenerateDeepEquals(typeof(Node))]
    [GenerateDeepEquals(typeof(Circle))]
    [GenerateDeepEquals(typeof(Cube))]
    [GenerateDeepEquals(typeof(Holder))]
    [GenerateDeepEquals(typeof(Empty))]
    [GenerateDeepEquals(typeof(Boxes))]
    [DeepEqualsSourceGenerationOptions(Hashing = DeepEqualsHashing.XxHash64)]
    public partial class Hash64FixtureContext : DeepEqualsContextBase
    {
    }

    /// <summary>The same closure retaining only the ancestors of the pair being compared.</summary>
    [GenerateDeepEquals(typeof(Person))]
    [GenerateDeepEquals(typeof(Node))]
    [GenerateDeepEquals(typeof(Circle))]
    [GenerateDeepEquals(typeof(Cube))]
    [GenerateDeepEquals(typeof(Holder))]
    [DeepEqualsSourceGenerationOptions(CycleHandling = DeepEqualsCycleHandling.Path)]
    public partial class PathFixtureContext : DeepEqualsContextBase
    {
    }
}
