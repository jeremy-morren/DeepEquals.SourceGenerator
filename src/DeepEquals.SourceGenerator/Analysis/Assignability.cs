// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DeepEquals.SourceGenerator.Analysis;

/// <summary>
/// Assignability as the strategy rules mean it: a value of <c>from</c> can stand where <c>to</c> is expected without changing its identity.
/// <see cref="Compilation.HasImplicitConversion"/> is deliberately not used,
/// because it also answers true for user-defined implicit operators (e.g. every primitive converts to <c>JsonNode</c>),
/// implicit numeric conversions (<c>int</c> to <c>long</c>) and nullable lifting, none of which should put a type under another type's comparer.
/// </summary>
internal static class Assignability
{
    /// <summary>Identity, implicit reference conversion, or boxing; nothing user-defined, numeric or nullable.</summary>
    public static bool IsAssignable(this Compilation compilation, ITypeSymbol from, ITypeSymbol to)
    {
        if (SymbolEqualityComparer.Default.Equals(from, to))
            return true;

        var conversion = compilation.ClassifyConversion(from, to);
        return conversion is { IsImplicit: true, IsUserDefined: false } && 
               (conversion.IsIdentity || conversion.IsReference || conversion.IsBoxing);
    }
}
