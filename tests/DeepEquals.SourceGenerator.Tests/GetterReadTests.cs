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
    public void Plain_and_record_auto_properties_are_read_through_their_getters()
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
        run.GeneratedSource.Should().Contain("x.X == y.X");
        run.GeneratedSource.Should().Contain("DecimalEquals(x.Amount, y.Amount)");
        run.GeneratedSource.Should().NotContain("UnsafeAccessorKind.Field", "nothing here needs an accessor");

        var pos = run.Comparer("Ctx", "Pos");
        run.Equals(pos, run.New("Pos", 1, "a"), run.New("Pos", 1, "a")).Should().BeTrue();
        run.Equals(pos, run.New("Pos", 1, "a"), run.New("Pos", 1, "b")).Should().BeFalse();
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
