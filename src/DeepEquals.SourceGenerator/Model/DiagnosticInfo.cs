// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace DeepEquals.SourceGenerator.Model;

/// <summary>A location that can live in the model: file path plus spans, enough to rebuild a <see cref="Location"/> in the output step.</summary>
internal sealed record LocationInfo(string FilePath, TextSpan TextSpan, LinePositionSpan LineSpan)
{
    public Location ToLocation() => Location.Create(FilePath, TextSpan, LineSpan);

    public static LocationInfo? From(Location? location)
    {
        if (location is null || !location.IsInSource)
        {
            return null;
        }

        return new LocationInfo(location.SourceTree!.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }

    public static LocationInfo? From(SyntaxNode? node) => node is null ? null : From(node.GetLocation());

    public static LocationInfo? From(ISymbol? symbol)
    {
        if (symbol is null)
        {
            return null;
        }

        foreach (Location location in symbol.Locations)
        {
            LocationInfo? info = From(location);
            if (info is not null)
            {
                return info;
            }
        }

        return null;
    }

    public static LocationInfo? From(AttributeData attribute) => From(attribute.ApplicationSyntaxReference?.GetSyntax());
}

/// <summary>A diagnostic captured in the model: descriptor, model-safe location and formatted arguments.</summary>
internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> Arguments)
{
    public static DiagnosticInfo Create(DiagnosticDescriptor descriptor, LocationInfo? location, params object?[] arguments)
        => new DiagnosticInfo(descriptor, location, new EquatableArray<string>(arguments.Select(a => a?.ToString() ?? string.Empty).ToArray()));

    public bool IsError => Descriptor.DefaultSeverity == DiagnosticSeverity.Error;

    public Diagnostic ToDiagnostic(LocationInfo? fallback)
        => Diagnostic.Create(Descriptor, (Location ?? fallback)?.ToLocation() ?? Microsoft.CodeAnalysis.Location.None, Arguments.ToArray());
}
