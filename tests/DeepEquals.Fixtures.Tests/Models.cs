// Copyright 2026 The DeepEquals source generator project contributors. All rights reserved.
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

#if NET5_0_OR_GREATER
    /// <summary>
    /// <see cref="Half"/> in every position the generator reads one from. .NET 5 has the type but no
    /// <c>BitConverter</c> method for its bits, so its generated code reinterprets them itself.
    /// </summary>
    public sealed class Halves
    {
        private Half _secret;

        public Halves(Half secret)
        {
            _secret = secret;
        }

        public Half Secret
        {
            get { return _secret; }
        }

        public Half Value;

        public Half? Maybe;

        public HalfPair Pair;

        public Half[] Values;

        public List<Half> List;

        public object Boxed;
    }

    public struct HalfPair
    {
        public Half A;
        public Half B;
    }

    [GenerateDeepEquals(typeof(Halves))]
    public partial class HalfFixtureContext : DeepEqualsContextBase
    {
    }
#endif

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

    /// <summary>The same closure with no pair table: one depth bound, for data that cannot hold cycles.</summary>
    [GenerateDeepEquals(typeof(Person))]
    [GenerateDeepEquals(typeof(Node))]
    [GenerateDeepEquals(typeof(Circle))]
    [GenerateDeepEquals(typeof(Cube))]
    [GenerateDeepEquals(typeof(Holder))]
    [DeepEqualsSourceGenerationOptions(CycleHandling = DeepEqualsCycleHandling.Tree, MaxDepth = 64)]
    public partial class TreeFixtureContext : DeepEqualsContextBase
    {
    }
}
