using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;

namespace DeepEquals.SourceGenerator.Analysis;

/// <summary>
/// The consumer's compilation imports only public and protected members from metadata, so private fields of referenced
/// types are invisible to GetMembers(). Analysis runs on a view created with MetadataImportOptions.All instead, one per
/// compilation snapshot: the table is keyed by the compilation instance, so it lives exactly as long as that snapshot.
/// </summary>
internal static class AllImportView
{
    private static readonly ConditionalWeakTable<Compilation, Compilation> s_views = new ConditionalWeakTable<Compilation, Compilation>();

    public static Compilation Get(Compilation compilation)
    {
        if (compilation.Options.MetadataImportOptions == MetadataImportOptions.All)
        {
            return compilation;
        }

        return s_views.GetValue(compilation, static c => c.WithOptions(c.Options.WithMetadataImportOptions(MetadataImportOptions.All)));
    }
}
