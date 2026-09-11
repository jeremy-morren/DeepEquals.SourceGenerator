// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using System.Linq;
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
            return null;

        // Abstract contexts contribute attributes to their derived contexts and nothing else.
        if (publicViewSymbol.IsAbstract)
            return null;

        // One transform owns the symbol: the earliest marked declaration, and the registrations provider when that
        // declaration carries both marker kinds.
        if (!IsCanonical(context, publicViewSymbol, marker)) 
            return null;

        cancellationToken.ThrowIfCancellationRequested();

        var compilation = AllImportView.Get(context.SemanticModel.Compilation);
        var symbol = compilation.GetSemanticModel(context.TargetNode.SyntaxTree).GetDeclaredSymbol(context.TargetNode, cancellationToken) as INamedTypeSymbol
                     ?? throw new InvalidOperationException("The context symbol could not be re-resolved in the All-import view.");

        var hintName = HintNamePrefixFor(symbol);
        var location = LocationInfo.From(context.TargetNode);
        var diagnostics = new List<DiagnosticInfo>();

        if (!ValidateDeclaration(symbol, (TypeDeclarationSyntax)context.TargetNode, compilation, location, diagnostics))
            return ContextModel.Failed(hintName, symbol.Name, location, EquatableArray.Create(diagnostics));

        var contextBase = CapabilityProbe.Find(compilation, KnownTypes.ContextBase)!;
        if (!FrameworkVersionMatches(contextBase.ContainingAssembly, location, diagnostics))
            return ContextModel.Failed(hintName, symbol.Name, location, EquatableArray.Create(diagnostics));

        var chain = ContextChain(symbol, contextBase);
        var options = OptionsReader.Read(chain, location, diagnostics);
        var languageVersion = ((CSharpParseOptions)context.TargetNode.SyntaxTree.Options).LanguageVersion;
        var capabilities = CapabilityProbe.Probe(compilation, languageVersion);

        cancellationToken.ThrowIfCancellationRequested();

        var registrations = Registrations.Collect(chain, compilation, location, diagnostics);
        var builder = new ClosureBuilder(compilation, symbol, options, capabilities, registrations, diagnostics, cancellationToken);
        var closure = builder.Build();
        if (closure.Failed) 
            return ContextModel.Failed(hintName, symbol.Name, location, EquatableArray.Create(diagnostics));

        var modelBuilder = new ModelBuilder(symbol, options, capabilities, closure, diagnostics, cancellationToken);
        return modelBuilder.Build(hintName, location);
    }

    /// <summary>
    /// The namespace-qualified name of the context, the stem every hint name of the context starts with: the context
    /// file is <c>{stem}.g.cs</c> and each type file <c>{stem}.{TypeName}.g.cs</c>, the layout System.Text.Json uses.
    /// Built from the raw symbol names, never a display string: a keyword name such as <c>@class</c> would put an
    /// <c>@</c> into a hint name, which Roslyn rejects. A context is non-generic, so every segment is an identifier.
    /// </summary>
    public static string HintNamePrefixFor(ISymbol symbol)
    {
        var segments = new List<string>();
        for (var type = symbol as INamedTypeSymbol; type is not null; type = type.ContainingType)
            segments.Add(type.Name);

        for (var ns = symbol.ContainingNamespace; ns is not null && !ns.IsGlobalNamespace; ns = ns.ContainingNamespace)
            segments.Add(ns.Name);

        segments.Reverse();
        return string.Join(".", segments);
    }

    /// <summary>
    /// The generator and the framework ship at the same version and must be used at the same version; nothing else in
    /// the generator reasons about older or newer framework assets. The informational version carries the prerelease
    /// tag, so it is compared when both sides have one; otherwise the three-part assembly version stands in.
    /// </summary>
    private static bool FrameworkVersionMatches(IAssemblySymbol framework, LocationInfo? location, List<DiagnosticInfo> diagnostics)
    {
        var informational = framework.GetAttributes()
            .Where(a => a.AttributeClass?.ToDisplayString() == "System.Reflection.AssemblyInformationalVersionAttribute")
            .Select(a => a.ConstructorArguments.Length == 1 ? a.ConstructorArguments[0].Value as string : null)
            .FirstOrDefault(v => !string.IsNullOrEmpty(v));

        var frameworkVersion = GeneratorInfo.WithoutBuildMetadata(informational) ?? framework.Identity.Version.ToString(3);
        var generatorVersion = GeneratorInfo.EffectiveVersion;
        if (informational is null)
        {
            // No informational version to compare: fall back to the three-part assembly version on both sides.
            var dash = generatorVersion.IndexOf('-');
            generatorVersion = dash < 0 ? generatorVersion : generatorVersion.Substring(0, dash);
        }

        if (frameworkVersion == generatorVersion)
            return true;

        diagnostics.Add(DiagnosticInfo.Create(Diagnostics.FrameworkVersionMismatch, location, frameworkVersion, generatorVersion));
        return false;
    }

    private static bool IsCanonical(GeneratorAttributeSyntaxContext context, INamedTypeSymbol symbol, MarkerKind marker)
    {
        // Order every declaration carrying either marker by (file path, span start) and find the earliest.
        SyntaxNode? earliest = null;
        string? earliestPath = null;
        var earliestStart = int.MaxValue;
        var earliestHasRegistration = false;
        var earliestHasOptions = false;

        foreach (var attribute in symbol.GetAttributes())
        {
            var name = attribute.AttributeClass?.ToDisplayString();
            var isRegistration = name == KnownTypes.GenerateDeepEqualsAttribute;
            var isOptions = name == KnownTypes.OptionsAttribute;
            if (!isRegistration && !isOptions)
                continue;

            var attributeSyntax = attribute.ApplicationSyntaxReference?.GetSyntax();
            var declaration = attributeSyntax?.FirstAncestorOrSelf<TypeDeclarationSyntax>();
            if (declaration is null) 
                continue;

            var path = declaration.SyntaxTree.FilePath;
            var start = declaration.SpanStart;
            var order = earliestPath is null ? -1 : string.CompareOrdinal(path, earliestPath);
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
            return false;

        // The registrations provider owns a declaration that carries both kinds; the options provider owns one that
        // carries only options, even when a later partial adds registrations.
        return marker == MarkerKind.Registration ? earliestHasRegistration : !earliestHasRegistration && earliestHasOptions;
    }

    private static bool ValidateDeclaration(INamedTypeSymbol symbol, TypeDeclarationSyntax node, Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics)
    {
        string? problem = null;
        if (node is not ClassDeclarationSyntax || symbol.IsRecord)
            problem = "a context must be an ordinary class";
        else if (symbol.IsStatic) 
            problem = "a context cannot be static";
        else if (symbol.IsGenericType) 
            problem = "a context cannot be generic";
        else if (IsFileLocal(symbol)) 
            problem = "a context cannot be file-local";
        else
            for (var outer = symbol.ContainingType; outer is not null; outer = outer.ContainingType)
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

        if (problem is null)
        {
            var contextBase = CapabilityProbe.Find(compilation, KnownTypes.ContextBase);
            if (contextBase is null || !InheritsFrom(symbol, contextBase))
                problem = "a context must derive from DeepEqualsContextBase";
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
    private static bool IsFileLocal(INamedTypeSymbol symbol) =>
        symbol.DeclaringSyntaxReferences
            .Any(r => r.GetSyntax() is TypeDeclarationSyntax t && t.Modifiers.Any(m => m.Text == "file"));

    private static bool InheritsFrom(INamedTypeSymbol symbol, INamedTypeSymbol baseType)
    {
        for (var current = symbol.BaseType; current is not null; current = current.BaseType)
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, baseType)) 
                return true;

        return false;
    }

    /// <summary>
    /// The context chain base-first: from the class directly below DeepEqualsContextBase down to the concrete context.
    /// </summary>
    private static List<INamedTypeSymbol> ContextChain(INamedTypeSymbol symbol, INamedTypeSymbol contextBase)
    {
        var chain = new List<INamedTypeSymbol>();
        for (var current = symbol; 
             current is not null && !SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, contextBase); 
             current = current.BaseType) 
            chain.Add(current);

        chain.Reverse();
        return chain;
    }
}
