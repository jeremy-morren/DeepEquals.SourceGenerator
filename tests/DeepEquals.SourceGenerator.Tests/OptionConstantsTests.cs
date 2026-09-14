// Copyright 2026 The DeepEquals source generator project contributors. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Linq;
using System.Reflection;
using DeepEquals.SourceGeneration;
using DeepEquals.SourceGenerator.Model;
using FluentAssertions;
using Xunit;

namespace DeepEquals.SourceGenerator.Tests;

/// <summary>
/// The generator cannot reference the framework, so the option defaults and limits the attribute documents are
/// written again in <see cref="ContextOptions"/>. These tests keep the two copies equal.
/// </summary>
public sealed class OptionConstantsTests
{
    private static (string Name, object? Value)[] Constants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && (f.Name.StartsWith("Default", StringComparison.Ordinal) || f.Name.StartsWith("Maximum", StringComparison.Ordinal)))
            .Select(f => (f.Name, f.GetRawConstantValue()))
            .OrderBy(c => c.Name, StringComparer.Ordinal)
            .ToArray();

    [Fact]
    public void Option_defaults_and_limits_match_the_attribute()
    {
        var framework = Constants(typeof(DeepEqualsSourceGenerationOptionsAttribute));
        framework.Should().NotBeEmpty();
        Constants(typeof(ContextOptions)).Should().Equal(framework);
    }

    [Fact]
    public void Cycle_handling_values_match_the_framework_enum()
    {
        Enum.GetNames(typeof(CycleHandling)).Should().Equal(Enum.GetNames(typeof(DeepEqualsCycleHandling)));
        Enum.GetValues(typeof(CycleHandling)).Cast<int>().Should().Equal(Enum.GetValues(typeof(DeepEqualsCycleHandling)).Cast<int>());
    }
}
