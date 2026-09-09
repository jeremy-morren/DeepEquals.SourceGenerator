// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System.Linq;
using Microsoft.CodeAnalysis;
using TypeKind = Microsoft.CodeAnalysis.TypeKind;

// ReSharper disable IdentifierTypo

namespace DeepEquals.SourceGenerator.Analysis;

/// <summary>
/// Language rules on what may be an <c>IEqualityComparer&lt;T&gt;</c> type argument. An interface with a <c>static abstract</c>
/// member that no interface in its hierarchy implements cannot be a type argument at all (CS8920), so the generator can never
/// build a comparer for it: the crawl skips it and a direct occurrence is a diagnostic.
/// </summary>
internal static class TypeArgumentRules
{
    /// <summary>True for an interface with an unimplemented <c>static abstract</c> member anywhere in its hierarchy.</summary>
    public static bool IsConstraintOnlyInterface(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol { TypeKind: TypeKind.Interface } iface) 
            return false;

        foreach (var source in iface.AllInterfaces.Concat([iface]))
            foreach (var member in source.GetMembers())
                if (member.IsStatic && member.IsAbstract && iface.FindImplementationForInterfaceMember(member) is null) 
                    return true;

        return false;
    }

    /// <summary>The first constraint-only interface in the type or its element and argument types, else null.</summary>
    public static INamedTypeSymbol? FindConstraintOnlyInterface(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => FindConstraintOnlyInterface(array.ElementType),
        INamedTypeSymbol named when IsConstraintOnlyInterface(named) => named,
        INamedTypeSymbol named => 
            named.TypeArguments.Select(FindConstraintOnlyInterface).FirstOrDefault(found => found is not null),
        _ => null,
    };
}
