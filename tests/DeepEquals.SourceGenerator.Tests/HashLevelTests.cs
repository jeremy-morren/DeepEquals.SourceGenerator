// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DeepEquals.SourceGenerator.Tests;

/// <summary>Cyclic graphs through nested containers and products: equal graphs, rolled or unrolled, must hash alike and terminate.</summary>
public sealed class HashLevelTests
{
    private const string Prelude = """
        using System;
        using System.Collections.Generic;
        using DeepEquals.SourceGeneration;
        namespace Tests;
        """;

    private static GeneratorRun Clean(string source)
    {
        var run = GeneratorHost.Run(Prelude + source);
        run.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        run.CompileErrors.Should().BeEmpty(run.GeneratedSource);
        return run;
    }

    private static void Set(object target, string member, object? value) => target.GetType().GetField(member)!.SetValue(target, value);

    [Fact]
    public void Nested_lists_tuples_and_dictionaries_in_a_cycle_terminate_and_hash_consistently()
    {
        var run = Clean("""
                        public sealed class N
                        {
                            public int V;
                            public List<List<N>>? Grid;
                            public (int, N?) Pair;
                            public (string, List<N>?) Bag;
                            public Dictionary<string, N>? Map;
                            public object? Any;
                        }

                        [GenerateDeepEquals(typeof(N))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        run.GeneratedSource.Should().Contain("ShallowHashCode_N", "N is cyclic and needs a level-0 hash");
        var comparer = run.Comparer("Ctx", "N");
        var n = run.Assembly!.GetTypes().Single(t => t.Name == "N");
        var pairType = typeof(ValueTuple<,>).MakeGenericType(typeof(int), n);
        var bagType = typeof(ValueTuple<,>).MakeGenericType(typeof(string), typeof(List<>).MakeGenericType(n));
        var listOfN = typeof(List<>).MakeGenericType(n);
        var listOfListOfN = typeof(List<>).MakeGenericType(listOfN);
        var mapType = typeof(Dictionary<,>).MakeGenericType(typeof(string), n);

        // A self-referential node: it sits in its own grid, its own pair, its own bag, its own map and its own object member.
        object Rolled(int v)
        {
            var node = Activator.CreateInstance(n)!;
            Set(node, "V", v);
            var inner = (System.Collections.IList)Activator.CreateInstance(listOfN)!;
            inner.Add(node);
            var grid = (System.Collections.IList)Activator.CreateInstance(listOfListOfN)!;
            grid.Add(inner);
            Set(node, "Grid", grid);
            Set(node, "Pair", Activator.CreateInstance(pairType, 1, node));
            Set(node, "Bag", Activator.CreateInstance(bagType, "b", inner));
            var map = (System.Collections.IDictionary)Activator.CreateInstance(mapType)!;
            map["k"] = node;
            Set(node, "Map", map);
            Set(node, "Any", node);
            return node;
        }

        // The same shape unrolled twice: a and b point at each other everywhere.
        object[] Unrolled(int v)
        {
            var a = Activator.CreateInstance(n)!;
            var b = Activator.CreateInstance(n)!;
            Set(a, "V", v); Set(b, "V", v);
            foreach (var (self, other) in new[] { (a, b), (b, a) })
            {
                var inner = (System.Collections.IList)Activator.CreateInstance(listOfN)!;
                inner.Add(other);
                var grid = (System.Collections.IList)Activator.CreateInstance(listOfListOfN)!;
                grid.Add(inner);
                Set(self, "Grid", grid);
                Set(self, "Pair", Activator.CreateInstance(pairType, 1, other));
                Set(self, "Bag", Activator.CreateInstance(bagType, "b", inner));
                var map = (System.Collections.IDictionary)Activator.CreateInstance(mapType)!;
                map["k"] = other;
                Set(self, "Map", map);
                Set(self, "Any", other);
            }

            return [a, b];
        }

        var r1 = Rolled(7);
        var r2 = Rolled(7);
        var u = Unrolled(7);
        run.Equals(comparer, r1, r2).Should().BeTrue();
        run.Equals(comparer, r1, u[0]).Should().BeTrue("a rolled cycle equals its unrolled form");
        run.Hash(comparer, r1).Should().Be(run.Hash(comparer, r2));
        run.Hash(comparer, r1).Should().Be(run.Hash(comparer, u[0]), "equal graphs hash alike whatever their unrolling");
        run.Hash(comparer, u[0]).Should().Be(run.Hash(comparer, u[1]));

        var r3 = Rolled(8);
        run.Equals(comparer, r1, r3).Should().BeFalse();

        var objects = run.Comparer("Ctx", "Object");
        run.Equals(objects, r1, u[1]).Should().BeTrue("the same graphs behind object");
        run.Hash(objects, r1).Should().Be(run.Hash(objects, u[1]));
    }
}
