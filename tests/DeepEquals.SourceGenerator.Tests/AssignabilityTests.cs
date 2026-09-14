// Copyright 2026 The DeepEquals source generator project contributors. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using System.Linq;
using DeepEquals.SourceGenerator.Analysis;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DeepEquals.SourceGenerator.Tests;

/// <summary>
/// The hierarchy fast path of <see cref="Assignability"/> must agree with the compiler's classifier wherever it
/// answers, over every ordered pair of a type set that covers each rule: base chains, sealed and generic classes,
/// interfaces with and without variance, structs, enums, nullables, arrays, tuples, delegates and object.
/// </summary>
public sealed class AssignabilityTests
{
    private const string Source =
        """
        using System;
        using System.Collections.Generic;
        namespace T
        {
            public class Base { }
            public class Derived : Base, IFoo, IComparable<Derived> { public int CompareTo(Derived? o) => 0; }
            public sealed class Leaf : Derived { }
            public class Open<T> { }
            public class ClosedInt : Open<int> { }
            public interface IFoo { }
            public interface IBar : IFoo { }
            public interface IIn<in T> { }
            public interface IOut<out T> { }
            public class OutDerived : IOut<Derived>, IIn<Base> { }
            public struct S : IFoo { public int X; }
            public struct Plain { public int X; }
            public enum E : byte { A }
            public delegate void D();
            public class Holder
            {
                public object O; public Base B; public Derived Dd; public Leaf L; public Open<int> Oi; public Open<string> Os; public ClosedInt Ci;
                public IFoo F; public IBar Br; public IIn<Base> InB; public IIn<Derived> InD; public IOut<Base> OutB; public IOut<Derived> OutD; public OutDerived Od;
                public S St; public S? NSt; public Plain P; public Plain? NP; public E En; public E? NEn; public int I; public int? NI; public long Lo; public string Str;
                public D Del; public Delegate Dg; public MulticastDelegate Mdg; public ValueType Vt; public Enum Enm; public Array Arr;
                public int[] Ints; public Derived[] Ds; public Base[] Bs; public object[] Os2; public int[,] Grid;
                public IEnumerable<Derived> ED; public IEnumerable<Base> EB; public IList<Derived> LD; public IReadOnlyList<Base> RB; public List<Derived> LstD; public List<Base> LstB;
                public IEnumerable<int> EI; public IComparable<Derived> Cmp; public IEquatable<int> EqI; public ICloneable Cl;
                public (int, string) Tup; public Tuple<int, string> RTup; public KeyValuePair<int, string> Kvp; public Dictionary<string, int> Dict; public IDictionary<string, int> IDict;
                public HashSet<Derived> Set; public ISet<Derived> ISet; public IReadOnlySet<Derived> RSet; public IEnumerable<IFoo> EF;
            }
        }
        """;

    [Fact]
    public void Hierarchy_fast_path_agrees_with_the_compiler_on_every_pair()
    {
        var tree = CSharpSyntaxTree.ParseText(Source);
        var references = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") is string list
            ? list.Split(System.IO.Path.PathSeparator).Where(f => System.IO.Path.GetFileName(f) is var n && (n.StartsWith("System.", StringComparison.Ordinal) || n is "mscorlib.dll" or "netstandard.dll"))
                .Select(f => (MetadataReference)MetadataReference.CreateFromFile(f)).ToArray()
            : [];
        var compilation = CSharpCompilation.Create("Assignability", [tree], references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        var holder = compilation.GetTypeByMetadataName("T.Holder")!;
        var types = holder.GetMembers().OfType<IFieldSymbol>().Select(f => f.Type).ToList();
        types.Count.Should().BeGreaterThan(50);

        var disagreements = new List<string>();
        var decided = 0;
        var cache = new AssignabilityCache(compilation);
        foreach (var from in types)
        foreach (var to in types)
        {
            if (SymbolEqualityComparer.Default.Equals(from, to))
                continue;

            var conversion = compilation.ClassifyConversion(from, to);
            var expected = conversion is { IsImplicit: true, IsUserDefined: false } && (conversion.IsIdentity || conversion.IsReference || conversion.IsBoxing);
            var fast = Assignability.DecideByHierarchy(from, to);
            if (fast is { } answer)
            {
                decided++;
                if (answer != expected)
                    disagreements.Add($"{from.ToDisplayString()} -> {to.ToDisplayString()}: hierarchy {answer}, compiler {expected}");
            }

            compilation.IsAssignable(from, to).Should().Be(expected, $"{from.ToDisplayString()} -> {to.ToDisplayString()}");
            cache.IsAssignable(from, to).Should().Be(expected, $"the cache: {from.ToDisplayString()} -> {to.ToDisplayString()}");
        }

        disagreements.Should().BeEmpty();
        decided.Should().BeGreaterThan(types.Count * types.Count / 2, "the fast path should decide most pairs, or it saves nothing");
    }
}
