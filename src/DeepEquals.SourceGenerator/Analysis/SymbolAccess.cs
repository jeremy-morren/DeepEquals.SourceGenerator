// Copyright 2026 The DeepEquals source generator project contributors. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DeepEquals.SourceGenerator.Analysis;

/// <summary>
/// Symbol questions asked once per member of every closure type, answered without the compiler's general machinery
/// where the common case allows: <see cref="Compilation.IsSymbolAccessibleWithin"/> scans the compilation's references
/// on every call, and an attribute's display name is built on every comparison.
/// </summary>
internal static class SymbolAccess
{
    /// <summary>
    /// Whether <paramref name="symbol"/> is accessible from <paramref name="within"/>. A public symbol whose containing
    /// types are all public is accessible from anywhere a compilation can see it, which is where every symbol here
    /// comes from; everything else is the compiler's question.
    /// </summary>
    public static bool IsAccessibleWithin(Compilation compilation, ISymbol symbol, ISymbol within)
    {
        if (symbol.DeclaredAccessibility == Accessibility.Public)
        {
            var containing = symbol.ContainingType;
            while (containing is not null && containing.DeclaredAccessibility == Accessibility.Public)
                containing = containing.ContainingType;

            if (containing is null)
                return true;
        }

        return compilation.IsSymbolAccessibleWithin(symbol, within);
    }

    /// <summary>
    /// The attributes of <paramref name="symbol"/>. For a symbol declared in source, the compiler binds its attributes
    /// on the first request, once per compilation, so a declaration whose syntax carries no attribute list answers
    /// without binding; everything else, metadata and implicit symbols included, asks the compiler.
    /// </summary>
    public static ImmutableArray<AttributeData> Attributes(ISymbol symbol)
    {
        var references = symbol.DeclaringSyntaxReferences;
        if (references.Length == 0)
            return symbol.GetAttributes();

        foreach (var reference in references)
        {
            var node = reference.GetSyntax();
            var lists = node switch
            {
                MemberDeclarationSyntax member => member.AttributeLists,
                ParameterSyntax parameter => parameter.AttributeLists,
                VariableDeclaratorSyntax { Parent.Parent: MemberDeclarationSyntax member } => member.AttributeLists,
                AccessorDeclarationSyntax accessor => accessor.AttributeLists,
                TypeParameterSyntax typeParameter => typeParameter.AttributeLists,
                _ => (SyntaxList<AttributeListSyntax>?)null,
            };
            if (lists is null || lists.Value.Count > 0)
                return symbol.GetAttributes();
        }

        return ImmutableArray<AttributeData>.Empty;
    }

    /// <summary>Whether <paramref name="attribute"/> is the attribute class <paramref name="fullName"/> names, comparing the short name before building the full one.</summary>
    public static bool IsAttribute(AttributeData attribute, string fullName)
    {
        var attributeClass = attribute.AttributeClass;
        if (attributeClass is null)
            return false;

        var name = attributeClass.Name;
        return fullName.Length > name.Length &&
               fullName[fullName.Length - name.Length - 1] == '.' &&
               string.CompareOrdinal(fullName, fullName.Length - name.Length, name, 0, name.Length) == 0 &&
               attributeClass.ToDisplayString() == fullName;
    }

    /// <summary>The full name of <paramref name="attribute"/>'s class when its short name is one of <paramref name="candidates"/>' last segments; otherwise null, and no display string is built.</summary>
    public static string? AttributeName(AttributeData attribute, params string[] candidates)
    {
        var attributeClass = attribute.AttributeClass;
        if (attributeClass is null)
            return null;

        var name = attributeClass.Name;
        foreach (var candidate in candidates)
        {
            if (candidate.Length > name.Length &&
                candidate[candidate.Length - name.Length - 1] == '.' &&
                string.CompareOrdinal(candidate, candidate.Length - name.Length, name, 0, name.Length) == 0)
                return attributeClass.ToDisplayString();
        }

        return null;
    }
}
