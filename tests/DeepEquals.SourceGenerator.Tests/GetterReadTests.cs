// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System.Linq;
using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace DeepEquals.SourceGenerator.Tests;

/// <summary>
/// Compiler storage behind an auto-property is read through its getter only where that is exactly a read of the backing
/// field. Every case where a call could answer with something else must keep reading the storage.
/// </summary>
public sealed class GetterReadTests
{
    private const string Prelude =
        """
        using System;
        using System.Collections.Generic;
        using DeepEquals.SourceGeneration;
        namespace Tests;
        """;

    private static GeneratorRun Clean(string source, LanguageVersion languageVersion = LanguageVersion.Latest)
    {
        var run = GeneratorHost.Run(Prelude + source, languageVersion);
        run.GeneratorDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Should().BeEmpty();
        run.CompileErrors.Should().BeEmpty(run.GeneratedSource);
        run.Assembly.Should().NotBeNull();
        return run;
    }

    [Fact]
    public void Auto_properties_are_read_through_their_getters_and_decimals_in_place()
    {
        var run = Clean("""
                        public sealed class Plain { public int Id { get; set; } public string? Name { get; init; } }
                        public sealed record Pos(int X, string Y);
                        public readonly record struct Val(decimal Amount, string Currency);

                        [GenerateDeepEquals(typeof(Plain))]
                        [GenerateDeepEquals(typeof(Pos))]
                        [GenerateDeepEquals(typeof(Val))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        run.GeneratedSource.Should().Contain("x.Id == y.Id").And.Contain("x.Name == y.Name");
        run.GeneratedSource.Should().Contain("x.X == y.X").And.Contain("x.Currency == y.Currency");

        // A decimal is compared through its address, so it is read in place: a getter's copy stalls the 64-bit reads.
        run.GeneratedSource.Should().NotContain("DecimalEquals(x.Amount, y.Amount)")
            .And.Contain("Name = \"<Amount>k__BackingField\"")
            .And.Contain("DecimalEquals(Val_Amount(ref ");
        var val = run.Comparer("Ctx", "Val");
        run.Equals(val, run.New("Val", 1.5m, "EUR"), run.New("Val", 1.5m, "EUR")).Should().BeTrue();
        run.Equals(val, run.New("Val", 1.5m, "EUR"), run.New("Val", 1.50m, "EUR")).Should().BeFalse("the scale is part of the value");
        run.Hash(val, run.New("Val", 1.5m, "EUR")).Should().Be(run.Hash(val, run.New("Val", 1.5m, "EUR")));

        var pos = run.Comparer("Ctx", "Pos");
        run.Equals(pos, run.New("Pos", 1, "a"), run.New("Pos", 1, "a")).Should().BeTrue();
        run.Equals(pos, run.New("Pos", 1, "a"), run.New("Pos", 1, "b")).Should().BeFalse();
    }

    /// <summary>
    /// A struct-typed auto-property is read in place: through its getter every member access would copy the whole
    /// struct, and a nullable's payload would be copied once more by GetValueOrDefault().
    /// </summary>
    [Fact]
    public void Struct_auto_properties_and_nullable_payloads_are_read_in_place()
    {
        var run = Clean("""
                        public readonly record struct Point3(double X, double Y, double Z);
                        public readonly record struct Money(decimal Amount, string Currency);
                        public sealed record Person(string Name, Point3 Position, Money? Balance, int Age);

                        [GenerateDeepEquals(typeof(Person))]
                        [GenerateDeepEquals(typeof(Point3))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        var source = run.GeneratedSource;
        source.Should().Contain("Name = \"<Position>k__BackingField\"").And.Contain("Name = \"<Balance>k__BackingField\"")
            .And.NotContain("x.Position.X", "each access through the getter copies the struct")
            .And.NotContain("x.Balance.HasValue");
        source.Should().Contain("DeepEqualsHelpers.NullableValueRef(Person_Balance(")
            .And.NotContain("Balance.GetValueOrDefault()", "the payload is compared where it lives");
        source.Should().Contain("x.Name == y.Name").And.Contain("x.Age == y.Age", "primitives and references keep their getters");

        // A struct comparer inlines into a direct caller, where its operands would otherwise be copied; a class comparer need not.
        System.Text.RegularExpressions.Regex.IsMatch(source, @"AggressiveInlining\)\]\s*public bool Equals\(global::Tests\.Point3 x").Should().BeTrue();
        System.Text.RegularExpressions.Regex.IsMatch(source, @"AggressiveInlining\)\]\s*public bool Equals\(global::Tests\.Person\? x").Should().BeFalse();

        var person = run.Comparer("Ctx", "Person");
        object Make(double x, decimal? amount) => run.New("Person", "p", run.New("Point3", x, 2.0, 3.0),
            amount is { } a ? run.New("Money", a, "EUR") : null, 40);
        run.Equals(person, Make(1.0, 1.5m), Make(1.0, 1.5m)).Should().BeTrue();
        run.Hash(person, Make(1.0, 1.5m)).Should().Be(run.Hash(person, Make(1.0, 1.5m)));
        run.Equals(person, Make(1.0, 1.5m), Make(1.0, 1.50m)).Should().BeFalse("the scale of the payload is compared");
        run.Equals(person, Make(1.0, 1.5m), Make(1.0, null)).Should().BeFalse();
        run.Equals(person, Make(1.0, null), Make(1.0, null)).Should().BeTrue();
        run.Equals(person, Make(0.0, null), Make(-0.0, null)).Should().BeFalse("the sign of zero is a bit");
    }

    [Fact]
    public void An_overridden_virtual_auto_property_keeps_the_base_storage()
    {
        var run = Clean("""
                        public class Base { public virtual int Size { get; set; } }
                        public sealed class Derived : Base
                        {
                            private int _shadow;
                            public override int Size { get => _shadow; set => _shadow = value; }
                        }

                        [GenerateDeepEquals(typeof(Derived))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        // The derived getter answers from _shadow; the base storage must still be compared on its own.
        var comparer = run.Comparer("Ctx", "Derived");
        var a = run.New("Derived");
        var b = run.New("Derived");
        SetBaseStorage(a, "Size", 1);
        SetBaseStorage(b, "Size", 2);
        run.Equals(comparer, a, b).Should().BeFalse("the base backing fields differ even though the derived getters agree");
        run.GeneratedSource.Should().NotContain("x.Size == y.Size");
    }

    [Fact]
    public void A_hiding_member_keeps_the_hidden_property_on_its_storage()
    {
        var run = Clean("""
                        public class Base { public int Size { get; set; } }
                        public sealed class Derived : Base { public new string? Size { get; set; } }

                        [GenerateDeepEquals(typeof(Derived))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """);

        var comparer = run.Comparer("Ctx", "Derived");
        var a = run.New("Derived");
        var b = run.New("Derived");
        SetBaseStorage(a, "Size", 1);
        SetBaseStorage(b, "Size", 2);
        run.Equals(comparer, a, b).Should().BeFalse("the hidden base property is compared through its own storage");
    }

    [Fact]
    public void A_getter_with_a_body_or_without_access_is_never_called()
    {
        var run = Clean("""
                        public sealed class Tricky
                        {
                            public int Counted { get { Calls++; return field; } set => field = value; }
                            public int Hidden { private get; set; }
                            public static int Calls;
                        }

                        [GenerateDeepEquals(typeof(Tricky))]
                        public partial class Ctx : DeepEqualsContextBase { }
                        """, LanguageVersion.Preview);

        var comparer = run.Comparer("Ctx", "Tricky");
        var a = run.New("Tricky");
        var b = run.New("Tricky");
        run.Equals(comparer, a, b).Should().BeTrue();
        run.Hash(comparer, a);
        ((int)run.Context("Tricky").GetField("Calls")!.GetValue(null)!).Should().Be(0, "a getter the user wrote is never invoked");

        run.GeneratedSource.Should().NotContain("x.Hidden", "a private getter cannot be called from the context");
    }

    private static void SetBaseStorage(object target, string property, object value)
    {
        var field = target.GetType().BaseType!.GetField($"<{property}>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        field.SetValue(target, value);
    }
}
