// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

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
    bool HasImmutableArrayAsSpan,
    bool HasUnsafeAccessor,
    bool HasGenericUnsafeAccessor,
    bool HasFrameworkSpanHelpers,
    bool HasFrameworkHash128,
    bool HasNullableGetValueRefOrDefaultRef,
    bool HasDecimalGetBitsSpan,
    bool HasRequiresUnreferencedCode,
    bool HasRequiresDynamicCode,
    bool HasUnconditionalSuppressMessage,
    bool HasFrameworkBlocks)
{
    /// <summary>Nullable annotations are emitted from C# 8 on; every generated file is otherwise C# 7.3-clean.</summary>
    public bool NullableAnnotations => LanguageVersion >= 800;

    /// <summary>A file-scoped <c>namespace X;</c> from C# 10 on, one indentation level less on every line.</summary>
    public bool FileScopedNamespace => LanguageVersion >= 1000;

    /// <summary>Sequences of bit-block elements compare as bytes and hash through XxHash3: the framework asset has the helpers and the target has spans.</summary>
    public bool BitBlocks => HasFrameworkBlocks && HasReadOnlySpan;

    public static readonly TargetCapabilities Empty = new(-1, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false, false);
}
