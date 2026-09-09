// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
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

        context.RegisterSourceOutput(registrations, static (spc, model) => Output(spc, model));
        context.RegisterSourceOutput(optionsOnly, static (spc, model) => Output(spc, model));
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
                ContextAnalyzer.HintNameFor(context.TargetSymbol),
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

    private static void Output(SourceProductionContext context, ContextModel model)
    {
        foreach (var diagnostic in model.Diagnostics)
            context.ReportDiagnostic(diagnostic.ToDiagnostic(model.CanonicalLocation));

        if (model.HasErrors)
            return;

        string source;
        try
        {
            source = Emitter.Emit(model, context.CancellationToken);
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

        context.AddSource(model.HintName, SourceText.From(source, System.Text.Encoding.UTF8));
    }
}

/// <summary>Which attribute provider produced a transform call.</summary>
internal enum MarkerKind
{
    Registration,
    Options,
}
