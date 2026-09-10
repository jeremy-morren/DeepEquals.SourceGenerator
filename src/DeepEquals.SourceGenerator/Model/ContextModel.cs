// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System.Linq;

namespace DeepEquals.SourceGenerator.Model;

/// <summary>
/// A delegate-accessor holder: one per declaring type that needs the fallback.
/// </summary>
internal sealed record AccessorHolderModel(string HolderName, string DeclaringTypeGlobalName, bool DeclaringTypeIsValueType, EquatableArray<MemberModel> Members);

/// <summary>
/// The complete, immutable, equatable description of one context. Equal models skip emission.
/// </summary>
internal sealed record ContextModel(
    string HintNamePrefix,
    string Namespace,
    EquatableArray<string> ContainingTypes,
    string Name,
    string Accessibility,
    LocationInfo? CanonicalLocation,
    ContextOptions Options,
    TargetCapabilities Capabilities,
    EquatableArray<TypeModel> Types,
    EquatableArray<CustomComparerModel> CustomComparers,
    EquatableArray<AccessorHolderModel> AccessorHolders,
    EquatableArray<string> SuppressedDiagnosticIds,
    bool HasUnsafeTypes,
    EquatableArray<DiagnosticInfo> Diagnostics)
{
    public bool HasErrors => Diagnostics.Any(d => d.IsError);

    /// <summary>A model that carries only diagnostics; nothing is emitted for it.</summary>
    public static ContextModel Failed(string hintNamePrefix, string name, LocationInfo? location, EquatableArray<DiagnosticInfo> diagnostics)
        => new(
            hintNamePrefix,
            string.Empty,
            EquatableArray<string>.Empty,
            name,
            "public",
            location,
            ContextOptions.Default,
            TargetCapabilities.Empty,
            EquatableArray<TypeModel>.Empty,
            EquatableArray<CustomComparerModel>.Empty,
            EquatableArray<AccessorHolderModel>.Empty,
            EquatableArray<string>.Empty,
            false,
            diagnostics);
}
