// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using DeepEquals.SourceGenerator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DeepEquals.SourceGenerator.Analysis;

/// <summary>Cheap work first: canonical declaration, validation and options, then the closure.</summary>
internal static class ContextAnalyzer
{
    public static ContextModel? Analyze(GeneratorAttributeSyntaxContext context, MarkerKind marker, CancellationToken cancellationToken)
    {
        if (context.TargetSymbol is not INamedTypeSymbol publicViewSymbol)
        {
            return null;
        }

        // Abstract contexts contribute attributes to their derived contexts and nothing else.
        if (publicViewSymbol.IsAbstract)
        {
            return null;
        }

        // One transform owns the symbol: the earliest marked declaration, and the registrations provider when that
        // declaration carries both marker kinds.
        if (!IsCanonical(context, publicViewSymbol, marker))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        Compilation compilation = AllImportView.Get(context.SemanticModel.Compilation);
        INamedTypeSymbol symbol = compilation.GetSemanticModel(context.TargetNode.SyntaxTree).GetDeclaredSymbol(context.TargetNode, cancellationToken) as INamedTypeSymbol
            ?? throw new InvalidOperationException("The context symbol could not be re-resolved in the All-import view.");

        string hintName = HintNameFor(symbol);
        LocationInfo? location = LocationInfo.From(context.TargetNode);
        List<DiagnosticInfo> diagnostics = new List<DiagnosticInfo>();

        if (!ValidateDeclaration(symbol, (TypeDeclarationSyntax)context.TargetNode, compilation, location, diagnostics))
        {
            return ContextModel.Failed(hintName, symbol.Name, location, EquatableArray.Create(diagnostics));
        }

        INamedTypeSymbol contextBase = CapabilityProbe.Find(compilation, KnownTypes.ContextBase)!;
        List<INamedTypeSymbol> chain = ContextChain(symbol, contextBase);
        ContextOptions options = OptionsReader.Read(chain, compilation, location, diagnostics);
        LanguageVersion languageVersion = ((CSharpParseOptions)context.TargetNode.SyntaxTree.Options).LanguageVersion;
        TargetCapabilities capabilities = CapabilityProbe.Probe(compilation, languageVersion);

        cancellationToken.ThrowIfCancellationRequested();

        Registrations registrations = Registrations.Collect(chain, compilation, location, diagnostics);
        ClosureBuilder builder = new ClosureBuilder(compilation, symbol, options, capabilities, registrations, diagnostics, cancellationToken);
        ClosureResult closure = builder.Build();
        if (closure.Failed)
        {
            return ContextModel.Failed(hintName, symbol.Name, location, EquatableArray.Create(diagnostics));
        }

        ModelBuilder modelBuilder = new ModelBuilder(compilation, symbol, options, capabilities, closure, diagnostics, cancellationToken);
        return modelBuilder.Build(hintName, location);
    }

    /// <summary>The context's namespace-qualified name plus a short hash of it; hint names must be unique per generator.</summary>
    public static string HintNameFor(ISymbol symbol)
    {
        string full = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (full.StartsWith("global::", StringComparison.Ordinal))
        {
            full = full.Substring("global::".Length);
        }

        using (SHA256 sha = SHA256.Create())
        {
            byte[] digest = sha.ComputeHash(Encoding.UTF8.GetBytes(full));
            StringBuilder hex = new StringBuilder(8);
            for (int i = 0; i < 4; i++)
            {
                hex.Append(digest[i].ToString("x2"));
            }

            return full.Replace('.', '_').Replace('+', '_') + "_" + hex + ".g.cs";
        }
    }

    private static bool IsCanonical(GeneratorAttributeSyntaxContext context, INamedTypeSymbol symbol, MarkerKind marker)
    {
        // Order every declaration carrying either marker by (file path, span start) and find the earliest.
        SyntaxNode? earliest = null;
        string? earliestPath = null;
        int earliestStart = int.MaxValue;
        bool earliestHasRegistration = false;
        bool earliestHasOptions = false;

        foreach (AttributeData attribute in symbol.GetAttributes())
        {
            string? name = attribute.AttributeClass?.ToDisplayString();
            bool isRegistration = string.Equals(name, KnownTypes.GenerateDeepEqualsAttribute, StringComparison.Ordinal);
            bool isOptions = string.Equals(name, KnownTypes.OptionsAttribute, StringComparison.Ordinal);
            if (!isRegistration && !isOptions)
            {
                continue;
            }

            SyntaxNode? attributeSyntax = attribute.ApplicationSyntaxReference?.GetSyntax();
            TypeDeclarationSyntax? declaration = attributeSyntax?.FirstAncestorOrSelf<TypeDeclarationSyntax>();
            if (declaration is null)
            {
                continue;
            }

            string path = declaration.SyntaxTree.FilePath;
            int start = declaration.SpanStart;
            int order = earliestPath is null ? -1 : string.CompareOrdinal(path, earliestPath);
            if (earliest is null || order < 0 || (order == 0 && start < earliestStart))
            {
                earliest = declaration;
                earliestPath = path;
                earliestStart = start;
                earliestHasRegistration = isRegistration;
                earliestHasOptions = isOptions;
            }
            else if (order == 0 && start == earliestStart)
            {
                earliestHasRegistration |= isRegistration;
                earliestHasOptions |= isOptions;
            }
        }

        if (earliest is null || !ReferenceEquals(earliest, context.TargetNode))
        {
            return false;
        }

        // The registrations provider owns a declaration that carries both kinds; the options provider owns one that
        // carries only options, even when a later partial adds registrations.
        return marker == MarkerKind.Registration ? earliestHasRegistration : !earliestHasRegistration && earliestHasOptions;
    }

    private static bool ValidateDeclaration(INamedTypeSymbol symbol, TypeDeclarationSyntax node, Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics)
    {
        string? problem = null;
        if (node is not ClassDeclarationSyntax || symbol.IsRecord)
        {
            problem = "a context must be an ordinary class";
        }
        else if (symbol.IsStatic)
        {
            problem = "a context cannot be static";
        }
        else if (symbol.IsGenericType)
        {
            problem = "a context cannot be generic";
        }
        else if (IsFileLocal(symbol))
        {
            problem = "a context cannot be file-local";
        }
        else
        {
            for (INamedTypeSymbol? outer = symbol.ContainingType; outer is not null; outer = outer.ContainingType)
            {
                if (outer.IsGenericType)
                {
                    problem = $"the containing type '{outer.Name}' is generic";
                    break;
                }

                if (IsFileLocal(outer))
                {
                    problem = $"the containing type '{outer.Name}' is file-local";
                    break;
                }

                if (!outer.DeclaringSyntaxReferences.All(r => r.GetSyntax() is TypeDeclarationSyntax t && t.Modifiers.Any(SyntaxKind.PartialKeyword)))
                {
                    problem = $"the containing type '{outer.Name}' must be partial";
                    break;
                }
            }
        }

        if (problem is null)
        {
            INamedTypeSymbol? contextBase = CapabilityProbe.Find(compilation, KnownTypes.ContextBase);
            if (contextBase is null || !InheritsFrom(symbol, contextBase))
            {
                problem = "a context must derive from DeepEqualsContextBase";
            }
        }

        if (problem is not null)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.InvalidContextDeclaration, location, symbol.Name, problem));
            return false;
        }

        if (!symbol.DeclaringSyntaxReferences.All(r => r.GetSyntax() is TypeDeclarationSyntax t && t.Modifiers.Any(SyntaxKind.PartialKeyword)))
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.ContextNotPartial, location, symbol.Name));
            return false;
        }

        return true;
    }

    /// <summary>The Roslyn floor predates IsFileLocal; the modifier token is the same on every version.</summary>
    private static bool IsFileLocal(INamedTypeSymbol symbol)
        => symbol.DeclaringSyntaxReferences.Any(r => r.GetSyntax() is TypeDeclarationSyntax t && t.Modifiers.Any(m => m.Text == "file"));

    private static bool InheritsFrom(INamedTypeSymbol symbol, INamedTypeSymbol baseType)
    {
        for (INamedTypeSymbol? current = symbol.BaseType; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>The context chain base-first: from the class directly below DeepEqualsContextBase down to the concrete context.</summary>
    private static List<INamedTypeSymbol> ContextChain(INamedTypeSymbol symbol, INamedTypeSymbol contextBase)
    {
        List<INamedTypeSymbol> chain = new List<INamedTypeSymbol>();
        for (INamedTypeSymbol? current = symbol; current is not null && !SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, contextBase); current = current.BaseType)
        {
            chain.Add(current);
        }

        chain.Reverse();
        return chain;
    }
}
