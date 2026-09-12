// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

// ReSharper disable MemberCanBePrivate.Global
// ReSharper disable InconsistentNaming
// ReSharper disable UnusedMember.Global

namespace DeepEquals.SourceGenerator;

/// <summary>Metadata names the generator probes or emits. Nothing here references the framework assembly at compile time.</summary>
internal static class KnownTypes
{
    private const string FrameworkNamespace = "DeepEquals.SourceGeneration.Framework";

    /// <summary>The namespace a context file needs: the attributes and <c>DeepEqualsContextBase</c>. The runtime stays under <c>.Framework</c>.</summary>
    private const string UserNamespace = "DeepEquals.SourceGeneration";

    public const string GenerateDeepEqualsAttribute = $"{UserNamespace}.GenerateDeepEqualsAttribute";
    public const string OptionsAttribute = $"{UserNamespace}.DeepEqualsSourceGenerationOptionsAttribute";
    public const string SimpleTypeAttribute = $"{UserNamespace}.SimpleTypeAttribute";
    public const string CustomEqualityComparerAttribute = $"{UserNamespace}.CustomEqualityComparerAttribute";
    public const string IgnoreAttribute = $"{UserNamespace}.DeepEqualsIgnoreAttribute";
    public const string ContextBase = $"{UserNamespace}.DeepEqualsContextBase";
    public const string Collections = $"{FrameworkNamespace}.DeepEqualsCollections";
    public const string Helpers = $"{FrameworkNamespace}.DeepEqualsHelpers";
    public const string HashCode = $"{FrameworkNamespace}.DeepEqualsHashCode";
    public const string Blocks = $"{FrameworkNamespace}.DeepEqualsBlocks";
    public const string GlobalBlocks = $"global::{Blocks}";

    // Emitted, global::-qualified.
    public const string GlobalFramework = $"global::{FrameworkNamespace}";
    public const string GlobalState = $"{GlobalFramework}.DeepEqualsState";
    public const string GlobalHashCode = $"{GlobalFramework}.DeepEqualsHashCode";
    public const string GlobalHelpers = $"{GlobalFramework}.DeepEqualsHelpers";
    public const string GlobalCollections = $"{GlobalFramework}.DeepEqualsCollections";
    public const string GlobalUnordered = $"{GlobalFramework}.DeepEqualsUnordered";
    public const string GlobalReflection = $"{GlobalFramework}.DeepEqualsReflection";
    public const string GlobalFieldGetter = $"{GlobalFramework}.FieldGetter";
    public const string GlobalHashOps = $"{GlobalFramework}.IDeepEqualsHashOps";
    public const string GlobalElementOps = $"{GlobalFramework}.IDeepEqualsElementOps";
    public const string GlobalStatelessElementOps = $"{GlobalFramework}.IDeepEqualsStatelessElementOps";
    public const string GlobalDepthElementOps = $"{GlobalFramework}.IDeepEqualsDepthElementOps";
    public const string GlobalDepthHashOps = $"{GlobalFramework}.IDeepEqualsDepthHashOps";
    public const string GlobalUnknownTypeException = $"{GlobalFramework}.DeepEqualsUnknownTypeException";
    public const string GlobalRuntimeHelpers = "global::System.Runtime.CompilerServices.RuntimeHelpers";
    public const string GlobalUnsafe = "global::System.Runtime.CompilerServices.Unsafe";
    public const string GlobalUnsafeAccessor = "global::System.Runtime.CompilerServices.UnsafeAccessor";
    public const string GlobalUnsafeAccessorKind = "global::System.Runtime.CompilerServices.UnsafeAccessorKind";
    public const string GlobalEqualityComparer = "global::System.Collections.Generic.EqualityComparer";
    public const string GlobalIEqualityComparer = "global::System.Collections.Generic.IEqualityComparer";
    public const string GlobalStringComparison = "global::System.StringComparison";
    public const string GlobalBitConverter = "global::System.BitConverter";
    public const string GlobalType = "global::System.Type";
    public const string GlobalObject = "global::System.Object";

    // Probed API surface.
    public const string ReadOnlySpan = "System.ReadOnlySpan`1";
    public const string Memory = "System.Memory`1";
    public const string ReadOnlyMemory = "System.ReadOnlyMemory`1";
    public const string CollectionsMarshal = "System.Runtime.InteropServices.CollectionsMarshal";
    public const string MemoryMarshal = "System.Runtime.InteropServices.MemoryMarshal";
    public const string Unsafe = "System.Runtime.CompilerServices.Unsafe";
    public const string IReadOnlySet = "System.Collections.Generic.IReadOnlySet`1";
    public const string ImmutableArray = "System.Collections.Immutable.ImmutableArray`1";
    public const string RequiresUnreferencedCode = "System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute";
    public const string RequiresDynamicCode = "System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute";
    public const string UnconditionalSuppressMessage = "System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessageAttribute";
    public const string ReferenceAssemblyAttribute = "System.Runtime.CompilerServices.ReferenceAssemblyAttribute";
    public const string CompilerGeneratedAttribute = "System.Runtime.CompilerServices.CompilerGeneratedAttribute";
    public const string InlineArrayAttribute = "System.Runtime.CompilerServices.InlineArrayAttribute";
    public const string ObsoleteAttribute = "System.ObsoleteAttribute";
    public const string ExperimentalAttribute = "System.Diagnostics.CodeAnalysis.ExperimentalAttribute";
    public const string RequiresPreviewFeaturesAttribute = "System.Runtime.Versioning.RequiresPreviewFeaturesAttribute";
}
