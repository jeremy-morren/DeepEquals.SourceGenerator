namespace DeepEquals.SourceGenerator.Model;

/// <summary>A delegate-accessor holder: one per declaring type that needs the fallback.</summary>
internal sealed record AccessorHolderModel(string HolderName, string DeclaringTypeGlobalName, bool DeclaringTypeIsValueType, EquatableArray<MemberModel> Members);

/// <summary>The complete, immutable, equatable description of one context. Equal models skip emission.</summary>
internal sealed record ContextModel(
    string HintName,
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
    public bool HasErrors
    {
        get
        {
            foreach (DiagnosticInfo diagnostic in Diagnostics)
            {
                if (diagnostic.IsError)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>A model that carries only diagnostics; nothing is emitted for it.</summary>
    public static ContextModel Failed(string hintName, string name, LocationInfo? location, EquatableArray<DiagnosticInfo> diagnostics)
        => new ContextModel(
            hintName,
            string.Empty,
            EquatableArray<string>.Empty,
            name,
            "public",
            location,
            ContextOptions.Default,
            new TargetCapabilities(0, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false),
            EquatableArray<TypeModel>.Empty,
            EquatableArray<CustomComparerModel>.Empty,
            EquatableArray<AccessorHolderModel>.Empty,
            EquatableArray<string>.Empty,
            false,
            diagnostics);
}
