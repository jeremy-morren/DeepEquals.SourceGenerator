// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using DeepEquals.SourceGeneration.Framework;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Xunit;

namespace DeepEquals.SourceGenerator.Tests;

/// <summary>
/// Hashing and equality operate on storage bits, never on a numeric conversion: a semantic scan of every generated
/// tree, and the edges that rule protects.
/// </summary>
public sealed class RawBitsTests
{
    private const string Prelude =
        """
        using System;
        using System.Collections.Generic;
        using System.Collections.Immutable;
        using DeepEquals.SourceGeneration;
        namespace Tests;
        """;

    private static GeneratorRun Clean(string source)
    {
        var run = GeneratorHost.Run(Prelude + source);
        run.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        run.CompileErrors.Should().BeEmpty(run.GeneratedSource);
        run.Assembly.Should().NotBeNull();
        return run;
    }

    /// <summary>Pins the framework's hash seed, reached through reflection, so a hash comparison cannot fail by chance.</summary>
    internal static IDisposable Seed(uint seed)
    {
        var property = typeof(DeepEqualsHashCode).GetProperty("Seed", BindingFlags.NonPublic | BindingFlags.Static)!;
        var saved = (uint)property.GetValue(null)!;
        property.SetValue(null, seed);
        return new Restore(() => property.SetValue(null, saved));
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }

    private static void Set(object target, string member, object? value) => target.GetType().GetField(member)!.SetValue(target, value);

    [Fact]
    public void Generated_cores_contain_no_numeric_conversion()
    {
        var run = Clean($$"""
                          public struct Wrapped { public double Value; public override int GetHashCode() => Value.GetHashCode(); public override bool Equals(object? o) => o is Wrapped w && w.Value.Equals(Value); }
                          public sealed class Floats
                          {
                              public float F; public double D; public Half H; public decimal M; public float? MaybeF; public double? MaybeD; public decimal? MaybeM;
                              public System.Numerics.Vector3 V; public double[]? Ds; public List<float>? Fs; public Dictionary<decimal, double>? Map; public Wrapped W;
                          }
                          [GenerateDeepEquals(typeof(Floats))]
                          [SimpleType(typeof(Wrapped))]
                          public partial class Ctx : DeepEqualsContextBase { }
                          """);

        var offences = new List<string>();
        foreach (var tree in run.Result.GeneratedTrees)
        {
            var model = run.Output.GetSemanticModel(tree);
            foreach (var node in tree.GetRoot().DescendantNodes())
            {
                switch (node)
                {
                    case CastExpressionSyntax cast when IsFloatingOrDecimal(model.GetTypeInfo(cast.Expression).Type) && IsIntegral(model.GetTypeInfo(cast).Type):
                        offences.Add($"cast: {cast}");
                        break;

                    case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "GetHashCode" } access } invocation
                        when IsFloatingOrDecimal(model.GetTypeInfo(access.Expression).Type):
                        offences.Add($"GetHashCode: {invocation}");
                        break;

                    case InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "GetBits" } } bits:
                        offences.Add($"GetBits: {bits}");
                        break;
                }
            }
        }

        offences.Should().BeEmpty("hashing and equality operate on storage bits, never on a numeric conversion");

        static bool IsFloatingOrDecimal(ITypeSymbol? type)
        {
            if (type is null) return false;
            if (type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && type is INamedTypeSymbol { TypeArguments: [var inner] })
                type = inner;

            return type.SpecialType is SpecialType.System_Single or SpecialType.System_Double or SpecialType.System_Decimal || type.Name == "Half";
        }

        static bool IsIntegral(ITypeSymbol? type) => type?.SpecialType is SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32
            or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Byte or SpecialType.System_SByte;

        // Behaviour: the edges the rule protects.
        var floats = run.Comparer("Ctx", "Floats");
        var a = run.New("Floats"); var b = run.New("Floats");
        Set(a, "D", 0.0); Set(b, "D", -0.0);
        run.Equals(floats, a, b).Should().BeFalse();
        Set(b, "D", 0.0);
        Set(a, "F", float.NaN); Set(b, "F", BitConverter.Int32BitsToSingle(0x7FC00001));
        run.Equals(floats, a, b).Should().BeFalse("two NaN payloads differ");
        Set(b, "F", float.NaN);
        run.Equals(floats, a, b).Should().BeTrue("the same NaN payload is equal to itself");
        run.Hash(floats, a).Should().Be(run.Hash(floats, b));
    }
}
