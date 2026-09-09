namespace DeepEquals.SourceGenerator.Model;

/// <summary>What the consuming compilation exposes, decided by probing, never by target framework name.</summary>
internal sealed record TargetCapabilities(
    int LanguageVersion,
    bool HasReadOnlySpan,
    bool HasMemory,
    bool HasCollectionsMarshal,
    bool HasMemoryMarshal,
    bool HasIReadOnlySet,
    bool HasImmutableArray,
    bool HasUnsafeAccessor,
    bool HasGenericUnsafeAccessor,
    bool HasFrameworkSpanHelpers,
    bool HasFrameworkHash128,
    bool HasNullableGetValueRefOrDefaultRef,
    bool HasSingleToInt32Bits,
    bool HasDecimalGetBitsSpan,
    bool HasRequiresUnreferencedCode,
    bool HasRequiresDynamicCode,
    bool HasUnconditionalSuppressMessage)
{
    /// <summary>Nullable annotations are emitted from C# 8 on; every generated file is otherwise C# 7.3-clean.</summary>
    public bool NullableAnnotations => LanguageVersion >= 800;
}
