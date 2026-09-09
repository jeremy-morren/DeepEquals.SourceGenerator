using System;
using System.Collections.Generic;
using System.Linq;
using DeepEquals.SourceGenerator.Model;
using Microsoft.CodeAnalysis;

namespace DeepEquals.SourceGenerator.Analysis;

/// <summary>Reads [DeepEqualsSourceGenerationOptions] along the context chain, base first, so derived values win per property.</summary>
internal static class OptionsReader
{
    public static ContextOptions Read(List<INamedTypeSymbol> chain, Compilation compilation, LocationInfo? contextLocation, List<DiagnosticInfo> diagnostics)
    {
        int maxSwitchCases = ContextOptions.DefaultMaxSwitchCases;
        int maxCollisionRun = ContextOptions.DefaultMaxUnorderedCollisionRun;
        int maxPairs = ContextOptions.DefaultMaxComparisonPairs;
        int maxArity = ContextOptions.DefaultMaxBinaryExpressionArity;
        int structBytes = ContextOptions.DefaultStructPassByValueMaxByteSize;
        string[] prefixes = Array.Empty<string>();

        foreach (INamedTypeSymbol type in chain)
        {
            foreach (AttributeData attribute in type.GetAttributes())
            {
                if (!string.Equals(attribute.AttributeClass?.ToDisplayString(), KnownTypes.OptionsAttribute, StringComparison.Ordinal))
                {
                    continue;
                }

                // Only explicitly written named arguments participate, so "unset" differs from "set to the default".
                LocationInfo? location = LocationInfo.From(attribute) ?? contextLocation;
                foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
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
                    }
                }
            }
        }

        return new ContextOptions(maxSwitchCases, maxCollisionRun, maxPairs, maxArity, structBytes, new EquatableArray<string>(prefixes));
    }

    private static int ReadInt(KeyValuePair<string, TypedConstant> argument, int min, int max, int fallback, LocationInfo? location, List<DiagnosticInfo> diagnostics)
    {
        if (argument.Value.Value is int value && value >= min && value <= max)
        {
            return value;
        }

        diagnostics.Add(DiagnosticInfo.Create(
            Diagnostics.InvalidOption,
            location,
            $"{argument.Key} = {argument.Value.Value ?? "null"} is outside {min}..{(max == int.MaxValue ? "int.MaxValue" : max.ToString())}; the default {fallback} is used"));
        return fallback;
    }

    private static string[] ReadPrefixes(KeyValuePair<string, TypedConstant> argument, LocationInfo? location, List<DiagnosticInfo> diagnostics)
    {
        if (argument.Value.Kind != TypedConstantKind.Array)
        {
            return Array.Empty<string>();
        }

        List<string> result = new List<string>();
        foreach (TypedConstant element in argument.Value.Values)
        {
            if (element.Value is string prefix && prefix.Length > 0)
            {
                result.Add(prefix);
            }
            else
            {
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.InvalidOption, location, "ExcludeInterfacesByPrefix contains a null or empty prefix, which is ignored"));
            }
        }

        return result.Distinct(StringComparer.Ordinal).OrderBy(p => p, StringComparer.Ordinal).ToArray();
    }
}
