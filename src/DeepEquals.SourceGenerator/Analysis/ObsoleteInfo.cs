// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DeepEquals.SourceGenerator.Analysis;

/// <summary>
/// <c>[Obsolete]</c> as the compiler applies it. Naming a type in code is a use of every obsolete type the name is built
/// from: the type, its containing types, its type arguments and an array's element type. Inside a member or type that is
/// itself obsolete no use is reported, at warning or error level.
/// </summary>
internal static class ObsoleteInfo
{
    /// <summary>The <c>[Obsolete]</c> on <paramref name="symbol"/> itself, or null.</summary>
    public static AttributeData? Of(ISymbol? symbol) =>
        symbol?.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == KnownTypes.ObsoleteAttribute);

    /// <summary>An <c>[Obsolete(message, true)]</c>: its use is error CS0619, which no pragma suppresses.</summary>
    public static bool IsError(AttributeData? attribute) =>
        attribute is { ConstructorArguments.Length: > 1 } && attribute.ConstructorArguments[1].Value is true;

    /// <summary>True when <paramref name="symbol"/> or a type containing it is obsolete, so uses inside it are not reported.</summary>
    public static bool InObsoleteContext(ISymbol symbol)
    {
        for (var current = symbol; current is not null; current = current.ContainingType)
            if (Of(current) is not null)
                return true;

        return false;
    }

    /// <summary>
    /// The <c>[Obsolete]</c> that naming <paramref name="type"/> uses, error level first: on the type, a containing type, a
    /// type argument or an element type. Null when naming it uses nothing obsolete.
    /// </summary>
    public static AttributeData? Find(ITypeSymbol type)
    {
        var found = new List<AttributeData>();
        Collect(type, found, new HashSet<ITypeSymbol>(SymbolEqualityComparer.Default));
        return found.FirstOrDefault(IsError) ?? found.FirstOrDefault();
    }

    private static void Collect(ITypeSymbol type, List<AttributeData> found, HashSet<ITypeSymbol> seen)
    {
        if (!seen.Add(type))
            return;

        switch (type)
        {
            case IArrayTypeSymbol array:
                Collect(array.ElementType, found, seen);
                return;

            case INamedTypeSymbol named:
                for (var current = named; current is not null; current = current.ContainingType)
                    if (Of(current) is { } attribute)
                        found.Add(attribute);

                foreach (var argument in named.TypeArguments)
                    Collect(argument, found, seen);

                return;
        }
    }

    /// <summary>
    /// The attribute as C# source, to repeat on a generated comparer: the message, the error flag, and the
    /// <c>DiagnosticId</c> and <c>UrlFormat</c> a .NET 5 attribute may carry, each only when the original has it.
    /// </summary>
    public static string Source(AttributeData attribute)
    {
        var arguments = new List<string>();
        if (attribute.ConstructorArguments.Length > 0)
            arguments.Add(Literal(attribute.ConstructorArguments[0].Value as string));

        if (attribute.ConstructorArguments.Length > 1)
            arguments.Add(IsError(attribute) ? "true" : "false");

        foreach (var named in attribute.NamedArguments.OrderBy(n => n.Key, System.StringComparer.Ordinal))
            if (named is { Key: "DiagnosticId" or "UrlFormat", Value.Value: string value })
                arguments.Add($"{named.Key} = {Literal(value)}");

        var text = new StringBuilder("[global::System.Obsolete");
        if (arguments.Count > 0)
            text.Append('(').Append(string.Join(", ", arguments)).Append(')');

        return text.Append(']').ToString();
    }

    private static string Literal(string? value) =>
        value is null ? "null" : SymbolDisplay.FormatLiteral(value, quote: true);
}
