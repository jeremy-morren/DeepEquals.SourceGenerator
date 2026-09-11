// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using DeepEquals.SourceGenerator.Analysis;
using DeepEquals.SourceGenerator.Emit;
using DeepEquals.SourceGenerator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace DeepEquals.SourceGenerator;

/// <summary>
/// Emits deep, by-value IEqualityComparer&lt;T&gt; implementations for the types a context registers. Two attribute
/// providers discover contexts, one canonical declaration per context produces one equatable model, and an unchanged
/// model skips its output.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class DeepEqualsGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<ContextModel> registrations = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                KnownTypes.GenerateDeepEqualsAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, ct) => Transform(ctx, MarkerKind.Registration, ct))
            .Where(static m => m is not null)!;

        IncrementalValuesProvider<ContextModel> optionsOnly = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                KnownTypes.OptionsAttribute,
                static (node, _) => node is TypeDeclarationSyntax,
                static (ctx, ct) => Transform(ctx, MarkerKind.Options, ct))
            .Where(static m => m is not null)!;

        // Roslyn requires hint names to be unique across the compilation, case-insensitively, and each context
        // names its files after itself. Two contexts whose names differ only by case would collide, so the set of
        // colliding stems is computed over every context and each output consults it. The set is small and rarely
        // changes, so a context's output still reruns only when its own model changes.
        IncrementalValueProvider<EquatableArray<string>> collisions = registrations.Collect()
            .Combine(optionsOnly.Collect())
            .Select(static (pair, _) => CollidingHintNamePrefixes(pair.Left, pair.Right));

        context.RegisterSourceOutput(registrations.Combine(collisions), static (spc, pair) => Output(spc, pair.Left, pair.Right));
        context.RegisterSourceOutput(optionsOnly.Combine(collisions), static (spc, pair) => Output(spc, pair.Left, pair.Right));
    }

    private static ContextModel? Transform(GeneratorAttributeSyntaxContext context, MarkerKind marker, CancellationToken ct)
    {
        try
        {
            return ContextAnalyzer.Analyze(context, marker, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var name = context.TargetSymbol.Name;
            var location = LocationInfo.From(context.TargetNode);
            return ContextModel.Failed(
                ContextAnalyzer.HintNamePrefixFor(context.TargetSymbol),
                name,
                location,
                EquatableArray.Create(
                    DiagnosticInfo.Create(
                        Diagnostics.GeneratorFailed,
                        location,
                        "analysing",
                        name,
                        ex.GetType().FullName,
                        ex.Message)));
        }
    }

    /// <summary>The hint-name stems that another context's stem equals case-insensitively, sorted.</summary>
    private static EquatableArray<string> CollidingHintNamePrefixes(ImmutableArray<ContextModel> registrations, ImmutableArray<ContextModel> optionsOnly)
    {
        var prefixes = registrations.Select(m => m.HintNamePrefix)
            .Concat(optionsOnly.Select(m => m.HintNamePrefix))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var colliding = prefixes
            .GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .SelectMany(g => g)
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        return new EquatableArray<string>(colliding);
    }

    /// <summary>
    /// A stem that collides case-insensitively with another context's gets a digest of its exact spelling, so the two
    /// stay distinct however Roslyn compares them.
    /// </summary>
    internal static string UniqueHintNamePrefix(string prefix, EquatableArray<string> collisions)
    {
        for (var i = 0; i < collisions.Count; i++)
        {
            if (collisions[i] == prefix)
                return $"{prefix}_{Naming.Digest(prefix, 8)}";
        }

        return prefix;
    }

    private static void Output(SourceProductionContext context, ContextModel model, EquatableArray<string> collisions)
    {
        foreach (var diagnostic in model.Diagnostics)
            context.ReportDiagnostic(diagnostic.ToDiagnostic(model.CanonicalLocation));

        if (model.HasErrors)
            return;

        var prefix = UniqueHintNamePrefix(model.HintNamePrefix, collisions);
        if (prefix != model.HintNamePrefix)
            model = model with { HintNamePrefix = prefix };

        IReadOnlyList<GeneratedFile> files;
        try
        {
            files = Emitter.Emit(model, context.CancellationToken);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Location data was captured before emission began; nothing here re-enters the failing path.
            context.ReportDiagnostic(DiagnosticInfo
                .Create(Diagnostics.GeneratorFailed, model.CanonicalLocation, "emitting", model.Name, ex.GetType().FullName, ex.Message)
                .ToDiagnostic(model.CanonicalLocation));
            return;
        }

        foreach (var file in files)
            context.AddSource(file.HintName, SourceText.From(file.Source, System.Text.Encoding.UTF8));
    }
}

/// <summary>Which attribute provider produced a transform call.</summary>
internal enum MarkerKind
{
    Registration,
    Options,
}
