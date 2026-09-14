// Copyright 2026 The DeepEquals source generator project contributors. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DeepEquals.SourceGenerator.Analysis;

/// <summary>
/// Assignability as the strategy rules mean it: a value of <c>from</c> can stand where <c>to</c> is expected without changing its identity.
/// <see cref="Compilation.HasImplicitConversion"/> is deliberately not used,
/// because it also answers true for user-defined implicit operators (e.g. every primitive converts to <c>JsonNode</c>),
/// implicit numeric conversions (<c>int</c> to <c>long</c>) and nullable lifting, none of which should put a type under another type's comparer.
/// </summary>
internal static class Assignability
{
    /// <summary>Identity, implicit reference conversion, or boxing; nothing user-defined, numeric or nullable.</summary>
    public static bool IsAssignable(this Compilation compilation, ITypeSymbol from, ITypeSymbol to)
    {
        if (SymbolEqualityComparer.Default.Equals(from, to))
            return true;

        // The type hierarchy answers nearly every pair; the compiler's classifier, which costs a thousand times more,
        // is asked only where variance or a nullable source is involved.
        if (DecideByHierarchy(from, to) is { } decided)
            return decided;

        var conversion = compilation.ClassifyConversion(from, to);
        return conversion is { IsImplicit: true, IsUserDefined: false } &&
               (conversion.IsIdentity || conversion.IsReference || conversion.IsBoxing);
    }

    /// <summary>
    /// The answer that follows from the type hierarchy alone, for two distinct types: a target class is reached through
    /// the source's base chain (boxing included, since a struct's base is <c>ValueType</c> and an array's is <c>Array</c>),
    /// a target interface through the source's interfaces, and <c>object</c> by everything. Null where only the
    /// classifier can tell: a nullable source, whose box reaches its payload's interfaces; a target array, which
    /// array covariance can reach; a variance conversion whose type arguments are arrays, type parameters or nested
    /// generic interfaces; and type parameters, <c>dynamic</c> and error types.
    /// </summary>
    internal static bool? DecideByHierarchy(ITypeSymbol from, ITypeSymbol to)
    {
        if (from.TypeKind is TypeKind.TypeParameter or TypeKind.Dynamic or TypeKind.Error or TypeKind.Pointer or TypeKind.FunctionPointer ||
            to.TypeKind is TypeKind.TypeParameter or TypeKind.Dynamic or TypeKind.Error or TypeKind.Pointer or TypeKind.FunctionPointer)
            return null;

        if (to.SpecialType == SpecialType.System_Object)
            return true;

        if (from is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
            return null;

        // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
        switch (to.TypeKind)
        {
            case TypeKind.Interface:
                // Only an implementation of the target's definition can reach it: at the same arguments directly, at
                // others through variance. The definition is compared first, since a full comparison of constructed
                // types costs more and nearly every pair fails here.
                var target = (INamedTypeSymbol)to;
                bool? variance = false;
                if (from is INamedTypeSymbol { TypeKind: TypeKind.Interface } self && SameDefinition(self, target))
                    variance = Merge(variance, ArgumentsConvert(self, target));

                foreach (var implemented in from.AllInterfaces)
                {
                    if (!SameDefinition(implemented, target))
                        continue;

                    if (SymbolEqualityComparer.Default.Equals(implemented, to))
                        return true;

                    variance = Merge(variance, ArgumentsConvert(implemented, target));
                }

                return variance;

            case TypeKind.Class:
                if (to.IsSealed)
                    return false;

                for (var @base = from.BaseType; @base is not null; @base = @base.BaseType)
                    if (SymbolEqualityComparer.Default.Equals(@base, to))
                        return true;

                return false;

            case TypeKind.Struct:
            case TypeKind.Enum:
            case TypeKind.Delegate:
                return false;   // no derived types, and a nullable target is a nullable conversion, which does not count

            default:
                return null;
        }
    }

    private static bool SameDefinition(INamedTypeSymbol a, INamedTypeSymbol b) =>
        SymbolEqualityComparer.Default.Equals(a.OriginalDefinition, b.OriginalDefinition);

    /// <summary>True beats everything, then null beats false: one implementation that converts is enough.</summary>
    private static bool? Merge(bool? a, bool? b) => a == true || b == true ? true : a is null || b is null ? null : false;

    /// <summary>
    /// Whether <paramref name="from"/>, an instantiation of <paramref name="to"/>'s definition, converts to it through
    /// variance: an <c>out</c> argument by reference conversion to <paramref name="to"/>'s, an <c>in</c> argument
    /// from it, an invariant one only by identity. Null where an argument's own conversion needs the classifier.
    /// </summary>
    private static bool? ArgumentsConvert(INamedTypeSymbol from, INamedTypeSymbol to)
    {
        if (to.ContainingType is { IsGenericType: true })
            return null;   // a nested generic interface carries its container's arguments too

        var parameters = to.OriginalDefinition.TypeParameters;
        var unknown = false;
        for (var i = 0; i < parameters.Length; i++)
        {
            var a = from.TypeArguments[i];
            var b = to.TypeArguments[i];
            if (SymbolEqualityComparer.Default.Equals(a, b))
                continue;

            var converts = parameters[i].Variance switch
            {
                VarianceKind.Out => ReferenceConvertible(a, b),
                VarianceKind.In => ReferenceConvertible(b, a),
                _ => false,
            };
            if (converts == false)
                return false;

            unknown |= converts is null;
        }

        return unknown ? null : true;
    }

    /// <summary>An implicit reference conversion, which variance requires: both sides reference types, no boxing.</summary>
    private static bool? ReferenceConvertible(ITypeSymbol a, ITypeSymbol b)
    {
        if (a.TypeKind == TypeKind.TypeParameter || b.TypeKind == TypeKind.TypeParameter)
            return null;

        if (!a.IsReferenceType || !b.IsReferenceType)
            return false;

        return DecideByHierarchy(a, b);
    }
}

/// <summary>
/// <see cref="Assignability.IsAssignable"/> for one run, with the sets that let most pairs be refused at once: a class
/// or interface target can be reached only from a source whose base chain or implemented interfaces include the
/// target's definition, so each source's definitions are collected once and a pair whose definitions cannot meet
/// never reaches the rule. A nullable source, a type parameter, or an array target, where the rule defers to the
/// classifier, is not filtered.
/// </summary>
internal sealed class AssignabilityCache(Compilation compilation)
{
    private readonly Dictionary<ITypeSymbol, HashSet<INamedTypeSymbol>> _definitions = new(SymbolEqualityComparer.Default);

    public bool IsAssignable(ITypeSymbol from, ITypeSymbol to)
    {
        if (to is INamedTypeSymbol { TypeKind: TypeKind.Class or TypeKind.Interface, SpecialType: not SpecialType.System_Object } target &&
            from.TypeKind is not (TypeKind.TypeParameter or TypeKind.Dynamic or TypeKind.Error) &&
            from is not INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } &&
            !Definitions(from).Contains(target.OriginalDefinition))
            return false;

        return compilation.IsAssignable(from, to);
    }

    /// <summary>The definitions of the source's base types and implemented interfaces, and its own.</summary>
    private HashSet<INamedTypeSymbol> Definitions(ITypeSymbol from)
    {
        if (_definitions.TryGetValue(from, out var definitions))
            return definitions;

        definitions = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
        if (from is INamedTypeSymbol named)
            definitions.Add(named.OriginalDefinition);

        for (var @base = from.BaseType; @base is not null; @base = @base.BaseType)
            definitions.Add(@base.OriginalDefinition);

        foreach (var implemented in from.AllInterfaces)
            definitions.Add(implemented.OriginalDefinition);

        _definitions[from] = definitions;
        return definitions;
    }
}
