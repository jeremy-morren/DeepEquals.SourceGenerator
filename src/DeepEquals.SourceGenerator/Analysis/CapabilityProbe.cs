// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Linq;
using DeepEquals.SourceGenerator.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DeepEquals.SourceGenerator.Analysis;

/// <summary>Decides every capability from the compilation's references, never from the target framework name.</summary>
internal static class CapabilityProbe
{
    public static TargetCapabilities Probe(Compilation compilation, LanguageVersion languageVersion)
    {
        // Runtime capabilities come from the identity of the core library, which a package cannot fake.
        IAssemblySymbol core = compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;
        bool runtimeFamily = string.Equals(core.Name, "System.Runtime", StringComparison.Ordinal) || string.Equals(core.Name, "System.Private.CoreLib", StringComparison.Ordinal);
        int coreMajor = runtimeFamily ? core.Identity.Version.Major : 0;
        bool hasUnsafeAccessor = coreMajor >= 8;
        bool hasGenericUnsafeAccessor = coreMajor >= 9;

        INamedTypeSymbol? collections = Find(compilation, KnownTypes.Collections);
        INamedTypeSymbol? hashCode = Find(compilation, KnownTypes.HashCode);
        // The framework asset a consumer resolves may predate a BCL type the consumer has (a net7.0 consumer gets the net6.0
        // asset, which has no Int128), so framework overloads are probed on the asset, never inferred from the BCL.
        bool hasHash128 = hashCode is not null && hashCode.GetMembers("Hash").OfType<IMethodSymbol>().Any(m => m.Parameters.Length == 1 && m.Parameters[0].Type.Name == "Int128");
        INamedTypeSymbol? bitConverter = Find(compilation, "System.BitConverter");
        INamedTypeSymbol? nullable = Find(compilation, "System.Nullable");
        INamedTypeSymbol? @decimal = Find(compilation, "System.Decimal");

        return new TargetCapabilities(
            LanguageVersion: (int)languageVersion,
            HasReadOnlySpan: Find(compilation, KnownTypes.ReadOnlySpan) is not null,
            HasMemory: Find(compilation, KnownTypes.Memory) is not null && Find(compilation, KnownTypes.ReadOnlyMemory) is not null,
            HasCollectionsMarshal: HasMethod(Find(compilation, KnownTypes.CollectionsMarshal), "AsSpan"),
            HasMemoryMarshal: HasMethod(Find(compilation, KnownTypes.MemoryMarshal), "Cast"),
            HasIReadOnlySet: Find(compilation, KnownTypes.IReadOnlySet) is not null,
            HasImmutableArray: Find(compilation, KnownTypes.ImmutableArray) is not null,
            HasUnsafeAccessor: hasUnsafeAccessor,
            HasGenericUnsafeAccessor: hasGenericUnsafeAccessor,
            HasFrameworkSpanHelpers: HasMethod(collections, "TryGetSpan"),
            HasFrameworkHash128: hasHash128,
            HasNullableGetValueRefOrDefaultRef: HasMethod(nullable, "GetValueRefOrDefaultRef"),
            HasSingleToInt32Bits: HasMethod(bitConverter, "SingleToInt32Bits"),
            HasDecimalGetBitsSpan: @decimal is not null && @decimal.GetMembers("GetBits").OfType<IMethodSymbol>().Any(m => m.Parameters.Length == 2),
            HasRequiresUnreferencedCode: Find(compilation, KnownTypes.RequiresUnreferencedCode) is not null,
            HasRequiresDynamicCode: Find(compilation, KnownTypes.RequiresDynamicCode) is not null,
            HasUnconditionalSuppressMessage: Find(compilation, KnownTypes.UnconditionalSuppressMessage) is not null);
    }

    /// <summary>
    /// The first accessible, non-error candidate for a metadata name, preferring the core library so that an in-box type
    /// beats a backfill package that defines the same name. Deterministic for a given set of references.
    /// </summary>
    public static INamedTypeSymbol? Find(Compilation compilation, string metadataName)
    {
        INamedTypeSymbol? best = null;
        foreach (INamedTypeSymbol candidate in compilation.GetTypesByMetadataName(metadataName))
        {
            if (candidate.TypeKind == Microsoft.CodeAnalysis.TypeKind.Error || !compilation.IsSymbolAccessibleWithin(candidate, compilation.Assembly))
            {
                continue;
            }

            if (best is null || Rank(compilation, candidate) < Rank(compilation, best))
            {
                best = candidate;
            }
        }

        return best;
    }

    private static int Rank(Compilation compilation, INamedTypeSymbol candidate)
    {
        IAssemblySymbol core = compilation.GetSpecialType(SpecialType.System_Object).ContainingAssembly;
        if (SymbolEqualityComparer.Default.Equals(candidate.ContainingAssembly, core))
        {
            return 0;
        }

        return SymbolEqualityComparer.Default.Equals(candidate.ContainingAssembly, compilation.Assembly) ? 2 : 1;
    }

    private static bool HasMethod(INamedTypeSymbol? type, string name)
        => type is not null && type.GetMembers(name).OfType<IMethodSymbol>().Any();
}
