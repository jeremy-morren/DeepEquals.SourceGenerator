// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using System.Linq;
using DeepEquals.SourceGenerator.Model;
using Microsoft.CodeAnalysis;
using TypeKind = Microsoft.CodeAnalysis.TypeKind;

namespace DeepEquals.SourceGenerator.Analysis;

/// <summary>One [CustomEqualityComparer] registration after resolution.</summary>
internal sealed class CustomRegistration
{
    public CustomRegistration(INamedTypeSymbol comparerType, ITypeSymbol target, bool handleNulls, string acquisition, bool holderTypedAsComparer, LocationInfo? location, int order)
    {
        ComparerType = comparerType;
        Target = target;
        HandleNulls = handleNulls;
        AcquisitionExpression = acquisition;
        HolderTypedAsComparer = holderTypedAsComparer;
        Location = location;
        Order = order;
    }

    public INamedTypeSymbol ComparerType { get; }

    public ITypeSymbol Target { get; }

    public bool HandleNulls { get; set; }

    public string AcquisitionExpression { get; }

    public bool HolderTypedAsComparer { get; }

    public LocationInfo? Location { get; }

    public int Order { get; }

    /// <summary>Set when a broader registration covers this one (DEQ030) or a duplicate precedes it (DEQ008).</summary>
    public bool Ignored { get; set; }

    /// <summary>Assigned once the registration is known to be emitted.</summary>
    public int Index { get; set; } = -1;
}

/// <summary>A context-level [DeepEqualsIgnore(typeof(T), "name")].</summary>
internal sealed class ContextIgnore
{
    public ContextIgnore(INamedTypeSymbol declaringType, string memberName, LocationInfo? location)
    {
        DeclaringType = declaringType;
        MemberName = memberName;
        Location = location;
    }

    public INamedTypeSymbol DeclaringType { get; }

    public string MemberName { get; }

    public LocationInfo? Location { get; }

    public bool Matched { get; set; }
}

/// <summary>Every strategy attribute along the context chain, validated and resolved at the symbol level.</summary>
internal sealed class Registrations
{
    public List<(INamedTypeSymbol Type, LocationInfo? Location)> Roots { get; } = [];

    public List<(ITypeSymbol Type, LocationInfo? Location)> SimpleTypes { get; } = [];

    public List<CustomRegistration> Custom { get; } = [];

    public List<ContextIgnore> ContextIgnores { get; } = [];

    public static Registrations Collect(List<INamedTypeSymbol> chain, Compilation compilation, LocationInfo? contextLocation, List<DiagnosticInfo> diagnostics)
    {
        var result = new Registrations();
        var order = 0;
        foreach (var type in chain)
        {
            foreach (var attribute in type.GetAttributes())
            {
                var name = attribute.AttributeClass?.ToDisplayString();
                var location = LocationInfo.From(attribute) ?? contextLocation;
                switch (name)
                {
                    case KnownTypes.GenerateDeepEqualsAttribute:
                        result.AddRoot(attribute, location, diagnostics);
                        break;
                    case KnownTypes.SimpleTypeAttribute:
                        result.AddSimple(attribute, location, diagnostics);
                        break;
                    case KnownTypes.CustomEqualityComparerAttribute:
                        result.AddCustom(attribute, compilation, location, diagnostics, order++);
                        break;
                    case KnownTypes.IgnoreAttribute:
                        if (attribute.ConstructorArguments is [{ Value: INamedTypeSymbol declaring }, { Value: string member }])
                            result.ContextIgnores.Add(new ContextIgnore(declaring, member, location));

                        break;
                }
            }
        }

        result.NormalizeCustom(compilation, diagnostics);
        result.NormalizeSimple(compilation, diagnostics);
        return result;
    }

    /// <summary>A [SimpleType] covered by a broader [SimpleType] is ignored (DEQ025); the result equals the broader rule alone.</summary>
    private void NormalizeSimple(Compilation compilation, List<DiagnosticInfo> diagnostics)
    {
        for (var i = SimpleTypes.Count - 1; i >= 0; i--)
        {
            var (narrow, location) = SimpleTypes[i];
            if (narrow.IsValueType && narrow.TypeKind != TypeKind.Interface) 
                continue;

            for (var j = 0; j < SimpleTypes.Count; j++)
            {
                if (i == j) 
                    continue;

                var broad = SimpleTypes[j].Type;
                if (SymbolEqualityComparer.Default.Equals(broad, narrow) ||
                    !compilation.IsAssignable(narrow, broad) ||
                    compilation.IsAssignable(broad, narrow))
                    continue;
                
                diagnostics.Add(DiagnosticInfo.Create(Diagnostics.SimpleTypeOverlap, location, narrow.ToDisplayString(), broad.ToDisplayString()));
                SimpleTypes.RemoveAt(i);
                break;
            }
        }
    }

    private void AddRoot(AttributeData attribute, LocationInfo? location, List<DiagnosticInfo> diagnostics)
    {
        if (attribute.ConstructorArguments.Length != 1)
            return;

        var type = attribute.ConstructorArguments[0].Value as ITypeSymbol;
        var problem = RegistrationProblem(type);
        if (problem is not null)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.InvalidRegisteredType, location, type?.ToDisplayString() ?? "?", problem));
            return;
        }

        switch (type)
        {
            case INamedTypeSymbol named:
            {
                if (!Roots.Any(r => SymbolEqualityComparer.Default.Equals(r.Type, named))) 
                    Roots.Add((named, location));
                break;
            }
            case IArrayTypeSymbol:
                // Arrays are valid roots; they are carried as their element's collection shape by the closure builder.
                diagnostics.Add(
                    DiagnosticInfo.Create(Diagnostics.InvalidRegisteredType, location, type.ToDisplayString(), "array roots are not supported yet; register the element type or a containing type"));
                break;
        }
    }

    private void AddSimple(AttributeData attribute, LocationInfo? location, List<DiagnosticInfo> diagnostics)
    {
        if (attribute.ConstructorArguments.Length != 1 || 
            attribute.ConstructorArguments[0].Value is not ITypeSymbol type) 
            return;

        var problem = RegistrationProblem(type);
        if (problem is not null)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.InvalidRegisteredType, location, type.ToDisplayString(), problem));
            return;
        }

        // SimpleType(typeof(S?)) is normalized to S before conflict detection.
        if (type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.SimpleTypeNullable, location, nullable.TypeArguments[0].ToDisplayString()));
            type = nullable.TypeArguments[0];
        }

        if (!SimpleTypes.Any(s => SymbolEqualityComparer.Default.Equals(s.Type, type))) 
            SimpleTypes.Add((type, location));
    }

    private void AddCustom(AttributeData attribute, Compilation compilation, LocationInfo? location, List<DiagnosticInfo> diagnostics, int order)
    {
        if (attribute.ConstructorArguments.Length == 0 || 
            attribute.ConstructorArguments[0].Value is not INamedTypeSymbol comparerType) 
            return;

        var memberName = attribute.ConstructorArguments.Length == 3 ? attribute.ConstructorArguments[1].Value as string : null;
        var handleNulls = attribute.ConstructorArguments[^1].Value is true;

        // T is the type argument of the one IEqualityComparer<T> the comparer implements.
        var comparerInterfaces = comparerType.AllInterfaces
            .Where(i => i.IsGenericType && BuiltInLeaves.FullMetadataName(i.OriginalDefinition) == "System.Collections.Generic.IEqualityComparer`1")
            .ToList();
        if (comparerInterfaces.Count != 1)
        {
            diagnostics.Add(DiagnosticInfo.Create(
                Diagnostics.CustomComparerInterfaceCount,
                location,
                comparerType.ToDisplayString(),
                comparerInterfaces.Count,
                string.Join(", ", comparerInterfaces.Select(i => i.ToDisplayString()))));
            return;
        }

        var target = comparerInterfaces[0].TypeArguments[0].WithNullableAnnotation(NullableAnnotation.None);
        if (target.SpecialType == SpecialType.System_Object)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.ObjectCustomComparer, location));
            return;
        }

        if (handleNulls &&
            target.IsValueType && 
            target is INamedTypeSymbol { OriginalDefinition.SpecialType: not SpecialType.System_Nullable_T })
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.HandleNullsOnValueType, location, target.ToDisplayString()));
            handleNulls = false;
        }

        if (!handleNulls && target is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableTarget)
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.NullableCustomComparerWithoutNulls, location, nullableTarget.TypeArguments[0].ToDisplayString()));

        var comparerGlobal = comparerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        string? acquisition = null;
        var typedAsComparer = false;
        string? problem = null;

        if (memberName is not null)
        {
            var candidates = comparerType.GetMembers(memberName)
                .Where(m => m.IsStatic && m.DeclaredAccessibility == Accessibility.Public)
                .Where(m => m is IFieldSymbol or IPropertySymbol { GetMethod: not null } or IMethodSymbol { Parameters.Length: 0, ReturnsVoid: false })
                .Where(m => compilation.IsAssignable(MemberType(m), comparerInterfaces[0]))
                .ToList();
            if (candidates.Count != 1)
            {
                problem = candidates.Count == 0
                    ? $"no public static field, property or parameterless method named '{memberName}' returns an IEqualityComparer<{target.ToDisplayString()}>"
                    : $"'{memberName}' is ambiguous";
            }
            else
            {
                var member = candidates[0];
                acquisition = member is IMethodSymbol ? $"{comparerGlobal}.{memberName}()" : $"{comparerGlobal}.{memberName}";
                typedAsComparer = SymbolEqualityComparer.Default.Equals(MemberType(member), comparerType);
            }
        }
        else
        {
            if (comparerType.TypeKind != TypeKind.Class) problem = "the comparer must be a class";
            else
            {
                var conventional = FindConventional(comparerType, comparerInterfaces[0], compilation, "Instance") 
                                   ?? FindConventional(comparerType, comparerInterfaces[0], compilation, "Default");
                if (conventional is not null)
                {
                    acquisition = $"{comparerGlobal}.{conventional.Name}";
                    typedAsComparer = SymbolEqualityComparer.Default.Equals(MemberType(conventional), comparerType);
                }
                else if (!comparerType.IsAbstract && comparerType.InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public))
                {
                    acquisition = $"new {comparerGlobal}()";
                    typedAsComparer = true;
                }
                else
                {
                    problem = comparerType.IsAbstract
                        ? "an abstract comparer needs the named static member form"
                        : "no public static Instance or Default member and no public parameterless constructor";
                }
            }
        }

        if (problem is not null)
        {
            diagnostics.Add(DiagnosticInfo.Create(Diagnostics.CustomComparerNoAcquisition, location, comparerType.ToDisplayString(), problem));
            return;
        }

        // The holder is typed as the concrete class only when both interface members bind to public methods on it.
        if (typedAsComparer && 
            !(BindsPublic(comparerType, "Equals", target, 2) && BindsPublic(comparerType, "GetHashCode", target, 1))) 
            typedAsComparer = false;

        Custom.Add(new CustomRegistration(comparerType, target, handleNulls, acquisition!, typedAsComparer, location, order));
    }

    private static ISymbol? FindConventional(INamedTypeSymbol comparerType, INamedTypeSymbol comparerInterface, Compilation compilation, string name)
        => comparerType.GetMembers(name)
            .FirstOrDefault(m => m.IsStatic && m.DeclaredAccessibility == Accessibility.Public
                && m is IFieldSymbol or IPropertySymbol { GetMethod: not null }
                && compilation.IsAssignable(MemberType(m), comparerInterface));

    private static ITypeSymbol MemberType(ISymbol member) => member switch
    {
        IFieldSymbol f => f.Type,
        IPropertySymbol p => p.Type,
        IMethodSymbol m => m.ReturnType,
        _ => throw new InvalidOperationException(),
    };

    private static bool BindsPublic(INamedTypeSymbol comparerType, string name, ITypeSymbol target, int arity)
    {
        for (var current = comparerType; current is not null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers(name).OfType<IMethodSymbol>())
            {
                if (method.IsStatic || 
                    method.DeclaredAccessibility != Accessibility.Public || 
                    method.Parameters.Length != arity || 
                    method.ExplicitInterfaceImplementations.Length > 0) 
                    continue;

                if (method.Parameters.All(p => SymbolEqualityComparer.Default.Equals(p.Type, target))) 
                    return true;
            }
        }

        return false;
    }

    /// <summary>Duplicates keep the first declaration (DEQ008); a broader reference registration hides a narrower one (DEQ030).</summary>
    private void NormalizeCustom(Compilation compilation, List<DiagnosticInfo> diagnostics)
    {
        for (var i = 0; i < Custom.Count; i++)
        {
            var a = Custom[i];
            if (a.Ignored) 
                continue;

            for (var j = i + 1; j < Custom.Count; j++)
            {
                var b = Custom[j];
                if (b.Ignored) 
                    continue;

                if (SymbolEqualityComparer.Default.Equals(a.Target, b.Target))
                {
                    b.Ignored = true;
                    diagnostics.Add(DiagnosticInfo.Create(Diagnostics.StrategyOverlap, b.Location, $"[CustomEqualityComparer] for '{b.Target.ToDisplayString()}' duplicates an earlier registration and is ignored"));
                    continue;
                }

                if (a.Target.IsValueType || b.Target.IsValueType)
                    continue;   // value-type targets are exact; nullable pairs stay independent

                if (compilation.IsAssignable(b.Target, a.Target))
                {
                    b.Ignored = true;
                    diagnostics.Add(DiagnosticInfo.Create(Diagnostics.CustomComparerOverlap, b.Location, b.Target.ToDisplayString(), a.Target.ToDisplayString()));
                }
                else if (compilation.IsAssignable(a.Target, b.Target))
                {
                    a.Ignored = true;
                    diagnostics.Add(DiagnosticInfo.Create(Diagnostics.CustomComparerOverlap, a.Location, a.Target.ToDisplayString(), b.Target.ToDisplayString()));
                    break;
                }
            }
        }
    }

    /// <summary>Why a type cannot be registered, or null.</summary>
    private static string? RegistrationProblem(ITypeSymbol? type)
    {
        if (type is null || type.TypeKind == TypeKind.Error)
            return "the type could not be resolved";

        if (type.SpecialType == SpecialType.System_Void) 
            return "void cannot be compared";

        if (type is IPointerTypeSymbol or IFunctionPointerTypeSymbol) 
            return "pointers cannot be compared";

        if (type.IsRefLikeType) 
            return "ref structs cannot be IEqualityComparer<T> arguments";

        if (type is INamedTypeSymbol named)
        {
            if (named.IsUnboundGenericType || named.TypeArguments.Any(a => a.TypeKind == TypeKind.TypeParameter))
                return "open generic types cannot be registered; register a constructed type";

            if (named.IsStatic) 
                return "static classes have no instances";

            if (named.TypeKind == TypeKind.Delegate) 
                return "delegates are not compared";
        }

        if (type is IArrayTypeSymbol { Rank: > 1 })
            return "multi-dimensional arrays are not supported";

        if (TypeArgumentRules.FindConstraintOnlyInterface(type) is { } constraintOnly)
            return $"'{constraintOnly.ToDisplayString()}' has a static abstract member without an implementation and cannot be an IEqualityComparer<T> type argument";
        
        if (type is ITypeParameterSymbol) 
            return "type parameters cannot be registered";

        return null;
    }
}
