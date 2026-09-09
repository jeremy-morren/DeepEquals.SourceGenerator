// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

// Trimming-analysis attributes for assets whose reference surface lacks them.
// The netstandard assets define them publicly so that other generators (PolySharp) see them in the consumer's compilation and do not emit duplicates;
// the net6.0 asset keeps RequiresDynamicCode internal because a public copy would conflict for a net7.0 consumer resolving the same asset.

// ReSharper disable All

#if !NET5_0_OR_GREATER
namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>Indicates that the specified method requires the ability to access members that trimming may remove.</summary>
    [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
    public sealed class RequiresUnreferencedCodeAttribute : Attribute
    {
        public RequiresUnreferencedCodeAttribute(string message)
        {
            Message = message;
        }

        public string Message { get; }

        public string? Url { get; set; }
    }

    /// <summary>Suppresses a trimming or AOT analysis diagnostic without being removed by the trimmer.</summary>
    [AttributeUsage(AttributeTargets.All, Inherited = false, AllowMultiple = true)]
    public sealed class UnconditionalSuppressMessageAttribute : Attribute
    {
        public UnconditionalSuppressMessageAttribute(string category, string checkId)
        {
            Category = category;
            CheckId = checkId;
        }

        public string Category { get; }

        public string CheckId { get; }

        public string? Scope { get; set; }

        public string? Target { get; set; }

        public string? MessageId { get; set; }

        public string? Justification { get; set; }
    }
}
#endif

#if !NET7_0_OR_GREATER
namespace System.Diagnostics.CodeAnalysis
{
    /// <summary>Indicates that the specified method requires dynamic code generation, which NativeAOT cannot provide.</summary>
    [AttributeUsage(AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Class, Inherited = false)]
#if NET6_0
    internal sealed class RequiresDynamicCodeAttribute : Attribute
#else
    public sealed class RequiresDynamicCodeAttribute : Attribute
#endif
    {
        public RequiresDynamicCodeAttribute(string message)
        {
            Message = message;
        }

        public string Message { get; }

        public string? Url { get; set; }
    }
}
#endif
