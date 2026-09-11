// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using System.Linq;
using DeepEquals.SourceGenerator.Model;
using Microsoft.CodeAnalysis;

namespace DeepEquals.SourceGenerator.Analysis;

/// <summary>
/// Reads [DeepEqualsSourceGenerationOptions] along the context chain,
/// base first, so derived values win per property.
/// </summary>
internal static class OptionsReader
{
    public static ContextOptions Read(List<INamedTypeSymbol> chain, LocationInfo? contextLocation, List<DiagnosticInfo> diagnostics)
    {
        var maxSwitchCases = ContextOptions.DefaultMaxSwitchCases;
        var maxCollisionRun = ContextOptions.DefaultMaxUnorderedCollisionRun;
        var maxPairs = ContextOptions.DefaultMaxComparisonPairs;
        var maxArity = ContextOptions.DefaultMaxBinaryExpressionArity;
        var structBytes = ContextOptions.DefaultStructPassByValueMaxByteSize;
        var prefixes = Array.Empty<string>();
        var cycleHandling = CycleHandling.Graph;
        var maxDepth = ContextOptions.DefaultMaxDepth;
        var matchingHashDepth = ContextOptions.DefaultMatchingHashDepth;
        var hashing = Hashing.XxHash32;
        LocationInfo? matchingHashDepthLocation = null;

        foreach (var type in chain)
        {
            foreach (var attribute in type.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString() != KnownTypes.OptionsAttribute)
                    continue;

                // Only explicitly written named arguments participate, so "unset" differs from "set to the default".
                var location = LocationInfo.From(attribute) ?? contextLocation;
                foreach (var argument in attribute.NamedArguments)
                {
                    switch (argument.Key)
                    {
                        case "MaxSwitchCases":
                            maxSwitchCases = ReadInt(argument, 1, int.MaxValue, ContextOptions.DefaultMaxSwitchCases, location, diagnostics);
                            break;
                        case "MaxUnorderedCollisionRun":
                            maxCollisionRun = ReadInt(argument, 1, ContextOptions.MaximumMaxUnorderedCollisionRun, ContextOptions.DefaultMaxUnorderedCollisionRun, location, diagnostics);
                            break;
                        case "MaxComparisonPairs":
                            maxPairs = ReadInt(argument, 1, ContextOptions.MaximumMaxComparisonPairs, ContextOptions.DefaultMaxComparisonPairs, location, diagnostics);
                            break;
                        case "MaxBinaryExpressionArity":
                            maxArity = ReadInt(argument, 1, int.MaxValue, ContextOptions.DefaultMaxBinaryExpressionArity, location, diagnostics);
                            break;
                        case "StructPassByValueMaxByteSize":
                            structBytes = ReadInt(argument, 0, int.MaxValue, ContextOptions.DefaultStructPassByValueMaxByteSize, location, diagnostics);
                            break;
                        case "ExcludeInterfacesByPrefix":
                            prefixes = ReadPrefixes(argument, location, diagnostics);
                            break;
                        case "CycleHandling":
                            cycleHandling = (CycleHandling)ReadInt(argument, (int)CycleHandling.Graph, (int)CycleHandling.Tree, (int)CycleHandling.Graph, location, diagnostics);
                            break;
                        case "MaxDepth":
                            maxDepth = ReadInt(argument, 1, ContextOptions.MaximumMaxDepth, ContextOptions.DefaultMaxDepth, location, diagnostics);
                            break;
                        case "MatchingHashDepth":
                            matchingHashDepth = ReadInt(argument, 1, ContextOptions.MaximumMatchingHashDepth, ContextOptions.DefaultMatchingHashDepth, location, diagnostics);
                            matchingHashDepthLocation = location;
                            break;
                        case "Hashing":
                            hashing = (Hashing)ReadInt(argument, (int)Hashing.XxHash32, (int)Hashing.XxHash64, (int)Hashing.XxHash32, location, diagnostics);
                            break;
                    }
                }
            }
        }

        // The fingerprint under Tree is the full depth-bounded hash, so the depth has nothing to act on. The value is
        // kept as written, so switching the mode back needs no edit.
        if (cycleHandling == CycleHandling.Tree && matchingHashDepthLocation is not null)
            diagnostics.Add(DiagnosticInfo.Create(
                Diagnostics.OptionWithoutEffect,
                matchingHashDepthLocation,
                "MatchingHashDepth has no effect under CycleHandling = Tree, where the matching fingerprint is the full hash; the value is ignored"));

        return new ContextOptions(
            maxSwitchCases,
            maxCollisionRun,
            maxPairs,
            maxArity,
            structBytes,
            new EquatableArray<string>(prefixes),
            cycleHandling,
            maxDepth,
            matchingHashDepth,
            hashing);
    }

    /// <summary>An integer or enum argument; an enum constant arrives as its underlying value, so one reader serves both.</summary>
    private static int ReadInt(KeyValuePair<string, TypedConstant> argument, int min, int max, int fallback, LocationInfo? location, List<DiagnosticInfo> diagnostics)
    {
        if (argument.Value.Value is int value && value >= min && value <= max)
            return value;

        diagnostics.Add(DiagnosticInfo.Create(
            Diagnostics.InvalidOption,
            location,
            $"{argument.Key} = {argument.Value.Value ?? "null"} is outside {min}..{(max == int.MaxValue ? "int.MaxValue" : max.ToString())}; the default {fallback} is used"));
        return fallback;
    }

    private static string[] ReadPrefixes(KeyValuePair<string, TypedConstant> argument, LocationInfo? location, List<DiagnosticInfo> diagnostics)
    {
        // A null array reads as empty: its Values are a default immutable array, which throws on every access.
        if (argument.Value.Kind != TypedConstantKind.Array || argument.Value.IsNull || argument.Value.Values.IsDefault)
            return [];

        var result = new List<string>(argument.Value.Values.Length);
        foreach (var element in argument.Value.Values)
        {
            if (element.Value is string { Length: > 0 } prefix)
                result.Add(prefix);
            else
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.InvalidOption, location, "ExcludeInterfacesByPrefix contains a null or empty prefix, which is ignored"));
        }

        return result
            .Distinct(StringComparer.Ordinal)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
    }
}
