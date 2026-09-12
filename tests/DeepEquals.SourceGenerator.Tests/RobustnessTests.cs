// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Xunit;

namespace DeepEquals.SourceGenerator.Tests;

/// <summary>Generated code under the consumer settings that break careless emitters: checked arithmetic, and a small thread stack.</summary>
public sealed class RobustnessTests
{
    private const string Prelude =
        """
        using System;
        using System.Collections.Generic;
        using DeepEquals.SourceGeneration;
        namespace Tests;
        """;

    /// <summary>
    /// A consumer that compiles with CheckForOverflowUnderflow must get the same answers: every hash word, fold and sum
    /// the emitter writes has to be unchecked on purpose. Collections, dispatch, aliasing and a boxed cycle, in every mode.
    /// </summary>
    [Theory]
    [InlineData("Graph")]
    [InlineData("Path")]
    [InlineData("Tree")]
    public void Generated_code_compiles_and_runs_under_CheckForOverflowUnderflow(string mode)
    {
        var run = GeneratorHost.Run(Prelude + $$"""
            public enum Big : ulong { Max = ulong.MaxValue }
            public enum Signed : int { Min = int.MinValue }
            public abstract class Shape { public long Id; }
            public sealed class Circle : Shape { public double R; }
            public sealed class Square : Shape { public decimal Side; }
            public struct Cell { public int V; public object? Link; }
            public sealed class Holder
            {
                public uint U; public ulong UL; public Big B; public Signed S; public nint N; public nuint UN; public float F; public Guid G; public DateTime T;
                public int[]? Ints; public List<long>? Longs; public HashSet<string>? Tags; public Dictionary<uint, Shape>? Shapes;
                public Shape? Shape; public object? Any; public Holder? Alias; public Cell Cell;
            }
            [GenerateDeepEquals(typeof(Holder))]
            [GenerateDeepEquals(typeof(Circle))]
            [GenerateDeepEquals(typeof(Square))]
            [DeepEqualsSourceGenerationOptions(CycleHandling = DeepEqualsCycleHandling.{{mode}})]
            public partial class Ctx : DeepEqualsContextBase { }
            """, checkedArithmetic: true);

        run.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        run.CompileErrors.Should().BeEmpty(run.GeneratedSource);

        object Holder(bool alias)
        {
            var h = run.New("Holder");
            var t = h.GetType();
            t.GetField("U")!.SetValue(h, uint.MaxValue);
            t.GetField("UL")!.SetValue(h, ulong.MaxValue);
            t.GetField("B")!.SetValue(h, Enum.ToObject(run.Assembly!.GetTypes().Single(x => x.Name == "Big"), ulong.MaxValue));
            t.GetField("S")!.SetValue(h, Enum.ToObject(run.Assembly!.GetTypes().Single(x => x.Name == "Signed"), int.MinValue));
            t.GetField("N")!.SetValue(h, (nint)(-1));
            t.GetField("UN")!.SetValue(h, nuint.MaxValue);
            t.GetField("F")!.SetValue(h, float.NaN);
            t.GetField("G")!.SetValue(h, new Guid("ffffffff-ffff-ffff-ffff-ffffffffffff"));
            t.GetField("T")!.SetValue(h, DateTime.MaxValue);
            t.GetField("Ints")!.SetValue(h, new[] { int.MinValue, int.MaxValue, -1 });
            t.GetField("Longs")!.SetValue(h, new List<long> { long.MinValue, long.MaxValue });
            t.GetField("Tags")!.SetValue(h, new HashSet<string> { "a", "b", "c" });
            var shapeType = run.Assembly!.GetTypes().Single(x => x.Name == "Shape");
            var shapes = (IDictionary)Activator.CreateInstance(typeof(Dictionary<,>).MakeGenericType(typeof(uint), shapeType))!;
            var circle = run.New("Circle");
            circle.GetType().GetField("R")!.SetValue(circle, -0.0);
            circle.GetType().GetField("Id")!.SetValue(circle, long.MinValue);
            shapes[uint.MaxValue] = circle;
            var square = run.New("Square");
            square.GetType().GetField("Side")!.SetValue(square, decimal.MinValue);
            shapes[0u] = square;
            t.GetField("Shapes")!.SetValue(h, shapes);
            t.GetField("Shape")!.SetValue(h, square);
            t.GetField("Any")!.SetValue(h, long.MinValue);
            if (alias) t.GetField("Alias")!.SetValue(h, Holder(alias: false));
            return h;
        }

        var comparer = run.Comparer("Ctx", "Holder");
        var a = Holder(alias: true);
        var b = Holder(alias: true);
        run.Equals(comparer, a, b).Should().BeTrue();
        run.Hash(comparer, a).Should().Be(run.Hash(comparer, b));
        a.GetType().GetField("U")!.SetValue(a, 0u);
        run.Equals(comparer, a, b).Should().BeFalse();
    }

    /// <summary>
    /// The audit's stack-safety scenario: an acyclic closure is as deep as its declarations, and nothing checks the stack
    /// there, so a long declaration chain has to fit a small thread stack by itself. Three hundred nested types, compared
    /// and hashed on a 256 KB thread, in every mode.
    /// </summary>
    [Theory]
    [InlineData("Graph")]
    [InlineData("Path")]
    [InlineData("Tree")]
    public void Deep_acyclic_declaration_chain_in_a_small_stack_thread(string mode)
    {
        const int depth = 300;
        var source = new StringBuilder(Prelude);
        for (var i = 0; i < depth; i++)
            source.AppendLine(i == depth - 1
                ? $"public sealed class L{i} {{ public int V; }}"
                : $"public sealed class L{i} {{ public int V; public L{i + 1}? Next; }}");

        source.AppendLine($"[GenerateDeepEquals(typeof(L0))] [DeepEqualsSourceGenerationOptions(CycleHandling = DeepEqualsCycleHandling.{mode})] public partial class Ctx : DeepEqualsContextBase {{ }}");
        var run = GeneratorHost.Run(source.ToString());
        run.CompileErrors.Should().BeEmpty();

        object Chain(int changeAt)
        {
            object? next = null;
            for (var i = depth - 1; i >= 0; i--)
            {
                var node = run.New($"L{i}");
                node.GetType().GetField("V")!.SetValue(node, i == changeAt ? -1 : i);
                if (next is not null) node.GetType().GetField("Next")!.SetValue(node, next);
                next = node;
            }

            return next!;
        }

        var comparer = run.Comparer("Ctx", "L0");
        var a = Chain(-1);
        var b = Chain(-1);
        var c = Chain(depth - 1);
        bool? same = null, different = null;
        int? hashA = null, hashB = null;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                same = run.Equals(comparer, a, b);
                different = run.Equals(comparer, a, c);
                hashA = run.Hash(comparer, a);
                hashB = run.Hash(comparer, b);
            }
            catch (Exception e)
            {
                failure = e;
            }
        }, maxStackSize: 256 * 1024);
        thread.Start();
        thread.Join();

        // A stack check that fires is an acceptable answer too; a crash would have taken the test host with it.
        if (failure is not null)
        {
            (failure.InnerException ?? failure).Should().BeOfType<InsufficientExecutionStackException>();
            return;
        }

        same.Should().BeTrue();
        different.Should().BeFalse("the last node differs");
        hashA.Should().Be(hashB);
    }
}
