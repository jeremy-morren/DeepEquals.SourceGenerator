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

    private static ContextModel? Transform(GeneratorAttributeSyntaxContext context, MarkerKind marker, CancellationToken cancellationToken)
    {
        try
        {
            return ContextAnalyzer.Analyze(context, marker, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            string name = context.TargetSymbol.Name;
            LocationInfo? location = LocationInfo.From(context.TargetNode);
            return ContextModel.Failed(
                ContextAnalyzer.HintNameFor(context.TargetSymbol),
                name,
                location,
                EquatableArray.Create(DiagnosticInfo.Create(Diagnostics.GeneratorFailed, location, "analysing", name, exception.GetType().FullName, exception.Message)));
        }
    }

    private static void Output(SourceProductionContext context, ContextModel model)
    {
        foreach (DiagnosticInfo diagnostic in model.Diagnostics)
        {
            context.ReportDiagnostic(diagnostic.ToDiagnostic(model.CanonicalLocation));
        }

        if (model.HasErrors)
        {
            return;
        }

        string source;
        try
        {
            source = Emitter.Emit(model, context.CancellationToken);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // Location data was captured before emission began; nothing here re-enters the failing path.
            context.ReportDiagnostic(DiagnosticInfo
                .Create(Diagnostics.GeneratorFailed, model.CanonicalLocation, "emitting", model.Name, exception.GetType().FullName, exception.Message)
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
