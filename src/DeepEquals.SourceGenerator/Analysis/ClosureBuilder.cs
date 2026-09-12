// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DeepEquals.SourceGenerator.Model;
using Microsoft.CodeAnalysis;
using TypeKind = DeepEquals.SourceGenerator.Model.TypeKind;
using RoslynTypeKind = Microsoft.CodeAnalysis.TypeKind;

// ReSharper disable SwitchStatementHandlesSomeKnownEnumValuesWithDefault
// ReSharper disable IdentifierTypo
// ReSharper disable PropertyCanBeMadeInitOnly.Global

namespace DeepEquals.SourceGenerator.Analysis;

/// <summary>
/// A closure type at the symbol level, before naming and graph analysis freeze it into a <see cref="TypeModel"/>.
/// </summary>
internal sealed class ClosureType
{
    public ClosureType(ITypeSymbol symbol, TypeKind kind)
    {
        Symbol = symbol;
        Kind = kind;
    }

    public ITypeSymbol Symbol { get; }

    public TypeKind Kind { get; }

    public int Id { get; set; } = -1;

    /// <summary>Reached from a root, member, element or strategy attribute; false for built-in leaves admitted to object dispatch only.</summary>
    public bool Reached { get; set; }

    public bool IsRoot { get; set; }

    /// <summary>A canonical container interface created for dispatch cases; the only container kind admitted as an assignable case.</summary>
    public bool IsCanonicalCase { get; set; }

    public LeafRule LeafRule { get; set; }

    public bool DefaultCompatible { get; set; }

    /// <summary>The type implements <c>IEquatable&lt;Self&gt;</c> exactly; an inherited <c>IEquatable&lt;Base&gt;</c> does not count.</summary>
    public bool ImplementsIEquatable { get; set; }

    /// <summary>The <c>IEquatable&lt;Self&gt;.Equals</c> implementation is a public instance method <c>Equals(Self)</c>, so <c>x.Equals(y)</c> binds to it.</summary>
    public bool HasPublicEquatableEquals { get; set; }

    public int Width { get; set; }

    public AggregateComponent[] AggregateComponents { get; set; } = [];

    public CustomRegistration? Custom { get; set; }

    /// <summary>The custom registration targets Nullable of this value type; values are wrapped for the comparer.</summary>
    public bool CustomWrapsNullable { get; set; }

    public ClosureType? Payload { get; set; }

    public ClosureType? Element { get; set; }

    public ClosureType? Key { get; set; }

    public ClosureType? Value { get; set; }

    public List<ClosureType> Items { get; } = [];

    public bool IsReadOnlyMemory { get; set; }

    public INamedTypeSymbol? CollectionInterface { get; set; }

    public List<ClosureMember> Members { get; } = [];

    public bool HasStorageIgnoredByShape { get; set; }

    /// <summary>
    /// Dispatch cases in emission order: assignable first, exact last. An exact case carries the index of the first
    /// assignable case its type converts to, or -1: that is the case the runtime type would have selected, so the emitter
    /// can test the exact type first and still land where the assignable chain would have.
    /// </summary>
        public List<(ClosureType Type, bool IsExact, int ResolvedAssignable, bool Hoistable)> Cases { get; } = [];

    public bool IsDispatchCapable => Kind == TypeKind.Dispatch || (Kind == TypeKind.Class && !Symbol.IsSealed);

    public bool IsDeep => Kind is TypeKind.Class or TypeKind.Struct;

    public bool IsValueShape => Kind is TypeKind.Struct or TypeKind.KeyValuePair or TypeKind.ValueTuple or TypeKind.Memory or TypeKind.ImmutableArray or TypeKind.ArraySegment;

    public bool IsContainer => Kind is TypeKind.Array or TypeKind.List or TypeKind.ImmutableArray or TypeKind.ArraySegment or TypeKind.Memory
        or TypeKind.ListInterface or TypeKind.EnumerableInterface or TypeKind.Set or TypeKind.Dictionary;

    public bool IsProduct => Kind is TypeKind.KeyValuePair or TypeKind.ValueTuple or TypeKind.Tuple;
}

/// <summary>A selected instance field at the symbol level.</summary>
internal sealed class ClosureMember
{
    public ClosureMember(IFieldSymbol field, string name, ClosureType type, MemberAccess access, MemberCost cost, int order)
    {
        Field = field;
        Name = name;
        Type = type;
        Access = access;
        Cost = cost;
        Order = order;
    }

    public IFieldSymbol Field { get; }

    /// <summary>The field name, or the associated property name for compiler storage.</summary>
    public string Name { get; }

    public ClosureType Type { get; }

    public MemberAccess Access { get; }

    public MemberCost Cost { get; }

    public int Order { get; }

    public INamedTypeSymbol Declaring => Field.ContainingType;
}

internal sealed class ClosureResult
{
    public ClosureResult(List<ClosureType> types, Registrations registrations, bool failed)
    {
        Types = types;
        Registrations = registrations;
        Failed = failed;
    }

    public List<ClosureType> Types { get; }

    public Registrations Registrations { get; }

    public bool Failed { get; }
}

/// <summary>
/// Builds the closure of the registered roots:
/// classification, member selection, the upward crawl and dispatch cases.
/// </summary>
internal sealed class ClosureBuilder
{
    private const int MaxGenericDepth = 8;
    private const int MaxTypes = 4096;

    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol _context;
    private readonly ContextOptions _options;
    private readonly TargetCapabilities _capabilities;
    private readonly Registrations _registrations;
    private readonly List<DiagnosticInfo> _diagnostics;
    private readonly CancellationToken _cancellationToken;
    private readonly Dictionary<ITypeSymbol, ClosureType> _types = new(SymbolEqualityComparer.Default);
    private readonly List<ClosureType> _ordered = [];
    private readonly Queue<(ClosureType Type, string Path)> _work = new();
    private readonly INamedTypeSymbol? _referenceAssemblyAttribute;
    private readonly INamedTypeSymbol? _ignoreAttribute;
    private readonly INamedTypeSymbol? _inlineArrayAttribute;
    // private readonly INamedTypeSymbol? _compilerGeneratedAttribute;
    // private readonly INamedTypeSymbol? _immutableArray;
    private readonly INamedTypeSymbol? _iReadOnlySet;
    // private readonly INamedTypeSymbol? _memory;
    // private readonly INamedTypeSymbol? _readOnlyMemory;
    // private readonly INamedTypeSymbol? _arraySegment;
    // private readonly INamedTypeSymbol? _tupleBase;
    private readonly bool _contextObsolete;
    private bool _failed;
    private bool _regexWarned;

    public ClosureBuilder(
        Compilation compilation, 
        INamedTypeSymbol context, 
        ContextOptions options, 
        TargetCapabilities capabilities, 
        Registrations registrations, 
        List<DiagnosticInfo> diagnostics, 
        CancellationToken cancellationToken)
    {
        _compilation = compilation;
        _context = context;
        _options = options;
        _capabilities = capabilities;
        _registrations = registrations;
        _diagnostics = diagnostics;
        _cancellationToken = cancellationToken;
        // Inside an obsolete context no use of an obsolete symbol is reported, error level included.
        _contextObsolete = ObsoleteInfo.InObsoleteContext(context);
        _referenceAssemblyAttribute = CapabilityProbe.Find(compilation, KnownTypes.ReferenceAssemblyAttribute);
        _ignoreAttribute = CapabilityProbe.Find(compilation, KnownTypes.IgnoreAttribute);
        _inlineArrayAttribute = CapabilityProbe.Find(compilation, KnownTypes.InlineArrayAttribute);
        _iReadOnlySet = CapabilityProbe.Find(compilation, KnownTypes.IReadOnlySet);
    }

    public ClosureResult Build()
    {
        // object is the closed-world root of every context.
        var objectType = Get(_compilation.GetSpecialType(SpecialType.System_Object), "object");
        objectType.Reached = true;

        foreach (var (type, _) in _registrations.Roots)
        {
            var root = Get(type, type.ToDisplayString());
            root.Reached = true;
            root.IsRoot = true;
        }

        foreach (var (type, _) in _registrations.SimpleTypes)
        {
            var simple = Get(type, type.ToDisplayString());
            simple.Reached = true;
        }

        foreach (var custom in _registrations.Custom)
        {
            if (custom.Ignored) 
                continue;

            var covered = Get(custom.Target, custom.Target.ToDisplayString());
            covered.Reached = true;
        }

        while (_work.Count > 0 && !_failed)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var (type, path) = _work.Dequeue();
            Expand(type, path);
        }

        if (_failed) 
            return new ClosureResult(_ordered, _registrations, failed: true);

        AdmitBuiltInLeaves();
        AddCanonicalContainerInterfaces();
        while (_work.Count > 0 && !_failed)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var (type, path) = _work.Dequeue();
            Expand(type, path);
        }

        if (_failed) 
            return new ClosureResult(_ordered, _registrations, failed: true);

        ComputeDispatchCases();
        ReportUnmatchedContextIgnores();
        return new ClosureResult(_ordered, _registrations, failed: false);
    }

    /// <summary>
    /// Behind a dispatch type, container cases are canonical per family and type arguments:
    /// IEnumerable{T} for every ordered container of T, ISet{T} (and IReadOnlySet{T} where it exists) for sets,
    /// IReadOnlyDictionary and IDictionary for dictionaries.
    /// The interface closure types those cases route to are created here so that an int[] and a List{int}
    /// behind object share one core and one hash formula.
    /// </summary>
    private void AddCanonicalContainerInterfaces()
    {
        var enumerable = _compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T);
        var set = CapabilityProbe.Find(_compilation, "System.Collections.Generic.ISet`1");
        var readOnlySet = _iReadOnlySet;
        var dictionary = CapabilityProbe.Find(_compilation, "System.Collections.Generic.IDictionary`2");
        var readOnlyDictionary = CapabilityProbe.Find(_compilation, "System.Collections.Generic.IReadOnlyDictionary`2");

        foreach (var container in _ordered.Where(t => t.IsContainer && t.Reached).ToList())
        {
            switch (container.Kind)
            {
                case TypeKind.Set:
                    if (set is not null) 
                        Canonical(set.Construct(container.Element!.Symbol));
                    if (readOnlySet is not null) 
                        Canonical(readOnlySet.Construct(container.Element!.Symbol));
                    break;
                
                case TypeKind.Dictionary:
                    if (readOnlyDictionary is not null) 
                        Canonical(readOnlyDictionary.Construct(container.Key!.Symbol, container.Value!.Symbol));
                    if (dictionary is not null) 
                        Canonical(dictionary.Construct(container.Key!.Symbol, container.Value!.Symbol));
                    break;
                
                default:
                    Canonical(enumerable.Construct(container.Element!.Symbol));
                    break;
            }
        }
    }

    private void Canonical(INamedTypeSymbol iface)
    {
        var type = Get(iface, iface.ToDisplayString());
        type.Reached = true;
        type.IsCanonicalCase = true;
    }

    // ----- lookup -------------------------------------------------------------------------------------------------------

    private ClosureType Get(ITypeSymbol symbol, string path)
    {
        symbol = symbol.WithNullableAnnotation(NullableAnnotation.None);
        if (_types.TryGetValue(symbol, out var existing)) 
            return existing;

        if (_ordered.Count >= MaxTypes)
        {
            Fail(Diagnostics.ClosureTooLarge, null, symbol.ToDisplayString(), path, $"more than {MaxTypes} types");
            return new ClosureType(symbol, TypeKind.Leaf);
        }

        if (GenericDepth(symbol) > MaxGenericDepth)
        {
            Fail(Diagnostics.ClosureTooLarge, null, symbol.ToDisplayString(), path, $"generic nesting deeper than {MaxGenericDepth}, which indicates expansive generic recursion");
            return new ClosureType(symbol, TypeKind.Leaf);
        }

        var created = Classify(symbol);
        _types.Add(symbol, created);
        _ordered.Add(created);
        _work.Enqueue((created, path));

        // Generated code names every closure type; an error-level [Obsolete] there is CS0619, which no pragma suppresses.
        if (!_contextObsolete && ObsoleteInfo.IsError(ObsoleteInfo.Find(symbol)))
            Fail(Diagnostics.ObsoleteErrorType, null, symbol.ToDisplayString(), path);

        return created;
    }

    private static int GenericDepth(ITypeSymbol symbol)
    {
        switch (symbol)
        {
            case IArrayTypeSymbol array:
                return 1 + GenericDepth(array.ElementType);
            
            case INamedTypeSymbol { IsGenericType: true } named:
                var max = named.TypeArguments.Select(GenericDepth).Prepend(0).Max();
                return 1 + max;
            
            default:
                return 0;
        }
    }

    private void Fail(DiagnosticDescriptor descriptor, LocationInfo? location, params object?[] args)
    {
        _diagnostics.Add(DiagnosticInfo.Create(descriptor, location, args));
        if (descriptor.DefaultSeverity == DiagnosticSeverity.Error)
            _failed = true;
    }

    // ----- classification ---------------------------------------------------------------------------------------------

    private ClosureType Classify(ITypeSymbol symbol)
    {
        // Custom comparers first: an exact registration, then a covering reference or interface registration.
        var custom = FindCustom(symbol, out var wrapsNullable);
        if (custom is not null)
        {
            if (IsUserSimple(symbol))
                _diagnostics.Add(DiagnosticInfo.Create(
                    Diagnostics.StrategyOverlap, custom.Location, $"'{symbol.ToDisplayString()}' is covered by both a [SimpleType] rule and the [CustomEqualityComparer] for '{custom.Target.ToDisplayString()}'; the custom comparer wins"));

            return new ClosureType(symbol, TypeKind.Leaf)
            {
                LeafRule = LeafRule.Custom, 
                Custom = custom, CustomWrapsNullable = wrapsNullable, 
                DefaultCompatible = false
            };
        }

        var builtIn = BuiltInLeaves.Find(symbol);
        if (builtIn is not null)
            return WithEquatable(new ClosureType(symbol, TypeKind.Leaf)
            {
                LeafRule = builtIn.Rule,
                DefaultCompatible = builtIn.DefaultCompatible,
                Width = builtIn.Width,
                AggregateComponents = builtIn.Components,
            });

        if (symbol.TypeKind == RoslynTypeKind.Enum) 
            return new ClosureType(symbol, TypeKind.Leaf)
            {
                LeafRule = LeafRule.Enum, 
                DefaultCompatible = true, 
                Width = EnumWidth(symbol)
            };

        if (symbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T }) 
            return new ClosureType(symbol, TypeKind.Nullable) { Payload = null };

        if (IsUserSimple(symbol)) 
            return WithEquatable(new ClosureType(symbol, TypeKind.Leaf)
            {
                LeafRule = LeafRule.UserSimple, 
                DefaultCompatible = true
            });

        if (symbol.SpecialType == SpecialType.System_Object) 
            return new ClosureType(symbol, TypeKind.Dispatch);

        switch (symbol)
        {
            case IArrayTypeSymbol:
                return new ClosureType(symbol, TypeKind.Array);
            
            case INamedTypeSymbol named:
            {
                var shape = ClassifyShape(named, out var collectionInterface, out var isReadOnlyMemory);
                if (shape is not null) 
                    return new ClosureType(symbol, shape.Value) { CollectionInterface = collectionInterface, IsReadOnlyMemory = isReadOnlyMemory };

                if (named.TypeKind == RoslynTypeKind.Interface || named.IsAbstract) 
                    return new ClosureType(symbol, TypeKind.Dispatch);

                return new ClosureType(symbol, named.IsValueType ? TypeKind.Struct : TypeKind.Class);
            }
            
            default:
                return new ClosureType(symbol, TypeKind.Dispatch);
        }
    }

    private CustomRegistration? FindCustom(ITypeSymbol symbol) => FindCustom(symbol, out _);

    private CustomRegistration? FindCustom(ITypeSymbol symbol, out bool wrapsNullable)
    {
        wrapsNullable = false;
        CustomRegistration? exact = null;
        CustomRegistration? covering = null;
        CustomRegistration? second = null;
        CustomRegistration? nullableOfSymbol = null;
        foreach (var registration in _registrations.Custom)
        {
            if (registration.Ignored) continue;

            if (SymbolEqualityComparer.Default.Equals(registration.Target, symbol))
            {
                exact = registration;
                break;
            }

            if (registration.Target.IsValueType)
            {
                // Value-type targets are exact only, except that a registration for S? also serves a non-nullable S by wrapping.
                if (symbol.IsValueType && 
                    registration.Target is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableTarget && 
                    SymbolEqualityComparer.Default.Equals(nullableTarget.TypeArguments[0], symbol)) 
                    nullableOfSymbol = registration;

                continue;
            }

            if (!_compilation.IsAssignable(symbol, registration.Target) ||
                symbol.IsValueType && registration.Target.SpecialType == SpecialType.System_Object)
                continue;
            
            if (covering is null) 
                covering = registration;
            else if (_compilation.IsAssignable(covering.Target, registration.Target))
            {} // covering is narrower; keep it
            else if (_compilation.IsAssignable(registration.Target, covering.Target)) 
                covering = registration;
            else 
                second = registration;
        }

        if (exact is not null) 
            return exact;

        if (nullableOfSymbol is not null)
        {
            wrapsNullable = true;
            return nullableOfSymbol;
        }

        if (covering is not null && second is not null)
            Fail(Diagnostics.AmbiguousCustomInterfaceComparers, covering.Location, symbol.ToDisplayString(), covering.Target.ToDisplayString(), second.Target.ToDisplayString());

        return covering;
    }

    private bool IsUserSimple(ITypeSymbol symbol)
    {
        foreach (var (simple, _) in _registrations.SimpleTypes)
        {
            if (SymbolEqualityComparer.Default.Equals(simple, symbol))
                return true;
                 
            if ((!symbol.IsValueType || simple.TypeKind == RoslynTypeKind.Interface || simple.TypeKind == RoslynTypeKind.Class) && 
                _compilation.IsAssignable(symbol, simple) &&
                simple.SpecialType != SpecialType.System_Object)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Records whether the leaf implements <c>IEquatable{Self}</c> exactly and, if so, whether that implementation is a public <c>Equals(Self)</c>.
    /// Only the exact self instantiation counts: a <c>Derived</c> inheriting <c>IEquatable{Base}</c> keeps <c>EqualityComparer{Derived}.Default</c>,
    /// whose interface dispatch it cannot be proved to match statically.
    /// </summary>
    private static ClosureType WithEquatable(ClosureType type)
    {
        var symbol = type.Symbol;
        foreach (var iface in symbol.AllInterfaces)
        {
            if (!iface.IsGenericType || 
                BuiltInLeaves.FullMetadataName(iface.OriginalDefinition) != "System.IEquatable`1" || 
                !SymbolEqualityComparer.Default.Equals(iface.TypeArguments[0], symbol))
                continue;

            type.ImplementsIEquatable = true;
            var interfaceEquals = iface.GetMembers("Equals").OfType<IMethodSymbol>().FirstOrDefault(m => m.Parameters.Length == 1);
            var implementation = interfaceEquals is null ? null : symbol.FindImplementationForInterfaceMember(interfaceEquals);
            type.HasPublicEquatableEquals = 
                implementation is IMethodSymbol
                {
                    DeclaredAccessibility: Accessibility.Public,
                    IsStatic: false,
                    MethodKind: MethodKind.Ordinary,
                    Name: "Equals",
                    Parameters: [{ RefKind: RefKind.None }],
                } method
                && SymbolEqualityComparer.Default.Equals(method.Parameters[0].Type, symbol);
            return type;
        }

        return type;
    }

    private static int EnumWidth(ITypeSymbol symbol)
        => ((INamedTypeSymbol)symbol).EnumUnderlyingType?.SpecialType switch
        {
            SpecialType.System_Byte or SpecialType.System_SByte => 1,
            SpecialType.System_Int16 or SpecialType.System_UInt16 => 2,
            SpecialType.System_Int64 or SpecialType.System_UInt64 => 8,
            _ => 4,
        };

    /// <summary>Shape precedence: dictionary, set, product and memory, ordered collection. Only generic interfaces count.</summary>
    private TypeKind? ClassifyShape(INamedTypeSymbol named, out INamedTypeSymbol? collectionInterface, out bool isReadOnlyMemory)
    {
        collectionInterface = null;
        isReadOnlyMemory = false;

        var dictionary = FindFamily(named, "System.Collections.Generic.IReadOnlyDictionary`2", "System.Collections.Generic.IDictionary`2");
        if (dictionary is not null)
        {
            collectionInterface = dictionary;
            return TypeKind.Dictionary;
        }

        var set = FindFamily(named, "System.Collections.Generic.IReadOnlySet`1", "System.Collections.Generic.ISet`1");
        if (set is not null)
        {
            collectionInterface = set;
            return TypeKind.Set;
        }

        var definition = named.OriginalDefinition;
        var metadata = BuiltInLeaves.FullMetadataName(definition);
        switch (metadata)
        {
            case "System.Collections.Generic.KeyValuePair`2":
                return TypeKind.KeyValuePair;
            
            case "System.ValueTuple`1":
            case "System.ValueTuple`2":
            case "System.ValueTuple`3":
            case "System.ValueTuple`4":
            case "System.ValueTuple`5":
            case "System.ValueTuple`6":
            case "System.ValueTuple`7":
            case "System.ValueTuple`8":
                return TypeKind.ValueTuple;
            
            case "System.Tuple`1":
            case "System.Tuple`2":
            case "System.Tuple`3":
            case "System.Tuple`4":
            case "System.Tuple`5":
            case "System.Tuple`6":
            case "System.Tuple`7":
            case "System.Tuple`8":
                return TypeKind.Tuple;
            
            case "System.Memory`1":
                return TypeKind.Memory;
            
            case "System.ReadOnlyMemory`1":
                isReadOnlyMemory = true;
                return TypeKind.Memory;
            
            case "System.Collections.Generic.List`1":
                return TypeKind.List;
            
            case "System.Collections.Immutable.ImmutableArray`1":
                return TypeKind.ImmutableArray;
            
            case "System.ArraySegment`1":
                return TypeKind.ArraySegment;
        }

        var list = FindFamily(named, "System.Collections.Generic.IReadOnlyList`1", "System.Collections.Generic.IList`1");
        if (list is not null)
        {
            collectionInterface = list;
            return TypeKind.ListInterface;
        }

        var enumerable = FindFamily(named, "System.Collections.Generic.IReadOnlyCollection`1", "System.Collections.Generic.IEnumerable`1");
        if (enumerable is not null)
        {
            collectionInterface = enumerable;
            return TypeKind.EnumerableInterface;
        }

        return null;
    }

    /// <summary>
    /// The first instantiation of the family on the type itself or in AllInterfaces; DEQ015 when instantiations disagree.
    /// </summary>
    private INamedTypeSymbol? FindFamily(INamedTypeSymbol named, params string[] metadataNames)
    {
        var found = new List<INamedTypeSymbol>(named.AllInterfaces.Length + 1);
        void Consider(INamedTypeSymbol candidate)
        {
            if (candidate.TypeKind != RoslynTypeKind.Interface || !candidate.IsGenericType) 
                return;

            var name = BuiltInLeaves.FullMetadataName(candidate.OriginalDefinition);
            if (metadataNames.Any(wanted => name == wanted))
                found.Add(candidate);
        }

        Consider(named);
        foreach (var iface in named.AllInterfaces) 
            Consider(iface);

        if (found.Count == 0)
            return null;

        // Prefer the most specific family member (first metadata name), then AllInterfaces order.
        var chosen = found[0];
        foreach (var wanted in metadataNames)
        {
            var match = found.FirstOrDefault(f => 
                BuiltInLeaves.FullMetadataName(f.OriginalDefinition) == wanted);
            if (match is null) continue;
            chosen = match;
            break;
        }

        // Instantiations of the winning family must agree on their type arguments.
        var distinct = found
            .Where(f => !f.TypeArguments.Zip(chosen.TypeArguments, 
                (a, b) => SymbolEqualityComparer.Default.Equals(a, b)).All(equal => equal))
            .ToList();
        if (distinct.Count > 0)
        {
            _diagnostics.Add(DiagnosticInfo.Create(
                Diagnostics.AmbiguousCollectionShape,
                LocationInfo.From(named),
                named.ToDisplayString(),
                metadataNames[^1],
                string.Join(", ", found.Select(f => f.ToDisplayString()).Distinct()),
                chosen.ToDisplayString()));
        }

        return chosen;
    }

    // ----- expansion --------------------------------------------------------------------------------------------------

    private void Expand(ClosureType type, string path)
    {
        var symbol = type.Symbol;
        switch (type.Kind)
        {
            case TypeKind.Leaf:
                if (type.LeafRule == LeafRule.Regex && !_regexWarned)
                {
                    _regexWarned = true;
                    _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.RegexReferenceEquality, null, path));
                }

                break;

            case TypeKind.Nullable:
                type.Payload = Child(((INamedTypeSymbol)symbol).TypeArguments[0], $"{path}?");
                break;

            case TypeKind.Array:
                var array = (IArrayTypeSymbol)symbol;
                if (array.Rank != 1)
                {
                    Fail(Diagnostics.MultiDimensionalArray, null, path, "?", symbol.ToDisplayString());
                    return;
                }

                type.Element = Child(array.ElementType, $"{path}[]");
                break;

            case TypeKind.List:
            case TypeKind.ImmutableArray:
            case TypeKind.ArraySegment:
            case TypeKind.Memory:
                type.Element = Child(((INamedTypeSymbol)symbol).TypeArguments[0], $"{path}<>");
                break;

            case TypeKind.ListInterface:
            case TypeKind.EnumerableInterface:
            case TypeKind.Set:
                type.Element = Child(type.CollectionInterface!.TypeArguments[0], $"{path}<>");
                WarnShapeIgnoresStorage(type);
                break;

            case TypeKind.Dictionary:
                type.Key = Child(type.CollectionInterface!.TypeArguments[0], $"{path}<key>");
                type.Value = Child(type.CollectionInterface!.TypeArguments[1], $"{path}<value>");
                WarnShapeIgnoresStorage(type);
                break;

            case TypeKind.KeyValuePair:
                type.Key = Child(((INamedTypeSymbol)symbol).TypeArguments[0], $"{path}.Key");
                type.Value = Child(((INamedTypeSymbol)symbol).TypeArguments[1], $"{path}.Value");
                break;

            case TypeKind.ValueTuple:
            case TypeKind.Tuple:
                foreach (var item in FlattenTuple((INamedTypeSymbol)symbol, type.Kind == TypeKind.ValueTuple))
                    type.Items.Add(Child(item, $"{path}.Item"));
                break;

            case TypeKind.Class:
            case TypeKind.Struct:
                SelectMembers(type, path);
                Crawl(type, path);
                break;

            case TypeKind.Dispatch:
                break;
        }
    }

    private ClosureType Child(ITypeSymbol symbol, string path)
    {
        var child = Get(symbol, path);
        child.Reached = true;
        return child;
    }

    private void WarnShapeIgnoresStorage(ClosureType type)
    {
        if (type.Symbol is not INamedTypeSymbol named || named.TypeKind == RoslynTypeKind.Interface)
            return;

        if (IsFrameworkType(named)) 
            return;

        var storage = new List<string>();
        for (var current = named; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            if (IsFrameworkType(current)) 
                break;

            foreach (var field in current.GetMembers().OfType<IFieldSymbol>())
                if (!field.IsStatic && !field.IsConst) 
                    storage.Add(field.AssociatedSymbol?.Name ?? field.Name);
        }

        if (storage.Count <= 0) return;
        
        type.HasStorageIgnoredByShape = true;
        _diagnostics.Add(DiagnosticInfo.Create(
            Diagnostics.CollectionShapeIgnoresStorage, LocationInfo.From(named), named.ToDisplayString(), type.CollectionInterface!.ToDisplayString(), string.Join(", ", storage)));
    }

    private static bool IsFrameworkType(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal);
    }

    private static IEnumerable<ITypeSymbol> FlattenTuple(INamedTypeSymbol tuple, bool valueTuple)
    {
        for (var i = 0; i < tuple.TypeArguments.Length; i++)
        {
            var argument = tuple.TypeArguments[i];
            if (i == 7 && 
                argument is INamedTypeSymbol { IsGenericType: true } rest && 
                BuiltInLeaves.FullMetadataName(rest.OriginalDefinition).StartsWith(valueTuple ? "System.ValueTuple`" : "System.Tuple`", StringComparison.Ordinal))
                foreach (var nested in FlattenTuple(rest, valueTuple))
                    yield return nested;
            else 
                yield return argument;
        }
    }

    // ----- member selection -------------------------------------------------------------------------------------------

    private void SelectMembers(ClosureType type, string path)
    {
        var named = (INamedTypeSymbol)type.Symbol;
        var fromReferenceAssembly = 
            _referenceAssemblyAttribute is not null && 
            named.ContainingAssembly.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, _referenceAssemblyAttribute));

        // Bases first, then the type; skip object.
        var chain = new List<INamedTypeSymbol>();
        for (var current = named; 
             current is not null && current.SpecialType != SpecialType.System_Object && current.SpecialType != SpecialType.System_ValueType;
             current = current.BaseType)
            chain.Add(current);

        chain.Reverse();

        if (fromReferenceAssembly)
        {
            if (type.Kind == TypeKind.Class)
            {
                Fail(Diagnostics.ReferenceAssemblyClass, LocationInfo.From(named), named.ToDisplayString());
                return;
            }

            var placeholdersOnly = named.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic).All(f => f.Name.StartsWith("_dummy", StringComparison.Ordinal));
            if (placeholdersOnly)
            {
                Fail(Diagnostics.ReferenceAssemblyClass, LocationInfo.From(named), named.ToDisplayString());
                return;
            }

            _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.ReferenceAssemblyStruct, LocationInfo.From(named), named.ToDisplayString()));
        }
        else if (IsFrameworkType(named)) 
            _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.FrameworkTypeWalked, null, named.ToDisplayString()));

        var order = 0;
        foreach (var declaring in chain)
        {
            var ignoredParameters = IgnoredPrimaryParameters(declaring);
            
            foreach (var parameter in ignoredParameters)
                if (!declaring.GetMembers($"<{parameter}>P").Any())
                    _diagnostics.Add(DiagnosticInfo.Create(
                        Diagnostics.IgnoreOnComputedMember, LocationInfo.From(declaring), parameter, declaring.ToDisplayString()));
            
            foreach (var member in declaring.GetMembers())
            {
                if (member is IPropertySymbol property && HasIgnore(property) && !HasBackingField(declaring, property))
                {
                    _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.IgnoreOnComputedMember, LocationInfo.From(property), property.Name, declaring.ToDisplayString()));
                    continue;
                }

                if (member is not IFieldSymbol field || field.IsStatic || field.IsConst) 
                    continue;

                var name = StorageName(field);
                if (field.AssociatedSymbol is IEventSymbol || field.Type.TypeKind == RoslynTypeKind.Delegate)
                {
                    _diagnostics.Add(DiagnosticInfo.Create(
                        Diagnostics.DelegateOrEventSkipped, 
                        LocationInfo.From(field.AssociatedSymbol ?? field), 
                        name, 
                        declaring.ToDisplayString()));
                    continue;
                }

                if (HasIgnore(field) || 
                    (field.AssociatedSymbol is not null && HasIgnore(field.AssociatedSymbol)) || 
                    IsContextIgnored(declaring, field, name) || 
                    ignoredParameters.Contains(name))
                    continue;

                if (ContainsDynamic(field.Type))
                {
                    _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.DynamicMember, LocationInfo.From(field.AssociatedSymbol ?? field), name, declaring.ToDisplayString()));
                    continue;
                }

                if (field.IsFixedSizeBuffer || IsInlineArray(field.Type))
                {
                    Fail(Diagnostics.UnsupportedStorage, LocationInfo.From(field.AssociatedSymbol ?? field), name, declaring.ToDisplayString());
                    continue;
                }

                if (field.RefKind != RefKind.None || field.Type is IPointerTypeSymbol || field.Type is IFunctionPointerTypeSymbol || field.Type.IsRefLikeType)
                {
                    Fail(Diagnostics.UnsupportedMemberType, 
                        LocationInfo.From(field.AssociatedSymbol ?? field), 
                        name, 
                        declaring.ToDisplayString(), 
                        field.Type.ToDisplayString(), 
                        "is a ref struct, pointer or ref field");
                    continue;
                }

                if (TypeArgumentRules.FindConstraintOnlyInterface(field.Type) is { } constraintOnly)
                {
                    var reason = SymbolEqualityComparer.Default.Equals(constraintOnly, field.Type)
                        ? "has a static abstract member without an implementation and cannot be an IEqualityComparer<T> type argument"
                        : $"contains '{constraintOnly.ToDisplayString()}', an interface with a static abstract member without an implementation that cannot be an IEqualityComparer<T> type argument";
                    Fail(Diagnostics.UnsupportedMemberType, 
                        LocationInfo.From(field.AssociatedSymbol ?? field), 
                        name,
                        declaring.ToDisplayString(), 
                        field.Type.ToDisplayString(),
                        reason);
                    continue;
                }

                if (field.Type is IArrayTypeSymbol { Rank: > 1 } && FindCustom(field.Type) is null)
                {
                    Fail(Diagnostics.MultiDimensionalArray, 
                        LocationInfo.From(field.AssociatedSymbol ?? field), 
                        name, 
                        declaring.ToDisplayString(), 
                        field.Type.ToDisplayString());
                    continue;
                }

                if (!IsNameable(field.Type))
                {
                    Fail(Diagnostics.InaccessibleMemberType,
                        LocationInfo.From(field.AssociatedSymbol ?? field),
                        name, 
                        declaring.ToDisplayString(),
                        field.Type.ToDisplayString());
                    continue;
                }

                var memberType = Child(field.Type, $"{path}.{name}");
                if (memberType.Kind == TypeKind.EnumerableInterface && ShouldWarnLazy(field.Type))
                    _diagnostics.Add(DiagnosticInfo.Create(
                        Diagnostics.PossiblyLazyEnumerable, LocationInfo.From(field.AssociatedSymbol ?? field), name, declaring.ToDisplayString(), field.Type.ToDisplayString()));

                // Compiler storage can never be read directly: C# cannot name <Name>k__BackingField even where the symbol is accessible.
                var compilerStorage = field.IsImplicitlyDeclared || field.Name.StartsWith("<", StringComparison.Ordinal);
                var genericDeclaring = IsGenericContext(declaring);
                // .NET 9 matches a generic accessor by position and constraints; a constraint the context cannot name falls back.
                var accessorAvailable = _capabilities.HasUnsafeAccessor &&
                    (!genericDeclaring || (_capabilities.HasGenericUnsafeAccessor && ConstraintsNameable(declaring)));

                // An [Obsolete] member at warning level is read as usual under the header's suppressions; one at error
                // level cannot be named at all, so its storage is read through an accessor or delegate, never the member.
                var obsoleteError = !_contextObsolete && (
                    ObsoleteInfo.IsError(ObsoleteInfo.Of(field)) ||
                    ObsoleteInfo.IsError(ObsoleteInfo.Of(field.AssociatedSymbol)) ||
                    field.AssociatedSymbol is IPropertySymbol { GetMethod: { } getter } && ObsoleteInfo.IsError(ObsoleteInfo.Of(getter)));

                MemberAccess access;
                if (!compilerStorage && !obsoleteError && _compilation.IsSymbolAccessibleWithin(field, _context))
                    access = MemberAccess.Direct;
                else if (compilerStorage && !obsoleteError && ReadableThroughGetter(field, type.Symbol) && !(accessorAvailable && ComparedInPlace(field.Type)))
                    access = MemberAccess.Getter;
                else if (accessorAvailable)
                    access = MemberAccess.UnsafeAccessor;
                else
                    access = MemberAccess.Delegate;


                type.Members.Add(new ClosureMember(field, name, memberType, access, CostOf(memberType), order++));
            }
        }

        // Cheapest first, stable within a class.
        type.Members.Sort((a, b) =>
        {
            var cost = a.Cost.CompareTo(b.Cost);
            return cost != 0 ? cost : a.Order.CompareTo(b.Order);
        });
    }

        /// <summary>
    /// True when reading the auto-property that owns <paramref name="field"/> through its getter is exactly a read of the
    /// field: the getter is the compiler's own, the context can call it, a call on a receiver of the declaring type cannot
    /// dispatch to an override, and the name resolves to this property from the owning type.
    /// </summary>
    /// <summary>
    /// A member of a value type wider than a register, read in place through <c>[UnsafeAccessor]</c> rather than through
    /// its getter. A getter returns a copy: once per access for a struct whose members are compared one by one, and
    /// for a decimal a copy the JIT spills field by field or with one wide store, on which the 64-bit reads of
    /// <c>DeepEqualsHelpers.DecimalEquals</c> stall. Measured against a record's own <c>Equals</c>, which reads its
    /// backing fields directly: 1.8 times its time through getters, less in place. Primitives keep their getters: a
    /// double read in place compiles to the same load, and would cost a small struct its inlining at the use site.
    /// </summary>
    private static bool ComparedInPlace(ITypeSymbol type) =>
        type.IsValueType && type.TypeKind != Microsoft.CodeAnalysis.TypeKind.Enum && type.SpecialType is not
            (SpecialType.System_Boolean or SpecialType.System_Char or SpecialType.System_SByte or SpecialType.System_Byte or
             SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or
             SpecialType.System_Int64 or SpecialType.System_UInt64 or SpecialType.System_Single or SpecialType.System_Double or
             SpecialType.System_IntPtr or SpecialType.System_UIntPtr);

    private bool ReadableThroughGetter(IFieldSymbol field, ITypeSymbol owner)
    {
        if (field.AssociatedSymbol is not IPropertySymbol { GetMethod: { } getter } property ||
            property.IsIndexer || property.ReturnsByRef || property.ReturnsByRefReadonly || property.IsStatic ||
            property.ExplicitInterfaceImplementations.Length > 0)
            return false;

        var declaring = field.ContainingType;
        var mayDispatch = property.IsVirtual || property.IsAbstract || (property.IsOverride && !property.IsSealed);
        if (mayDispatch && !declaring.IsSealed && !declaring.IsValueType)
            return false;   // a derived override would answer instead of this storage

        // A getter that is not readonly would be called on a defensive copy of an `in` struct receiver.
        if (declaring.IsValueType && !getter.IsReadOnly)
            return false;

        if (!_compilation.IsSymbolAccessibleWithin(property, _context) || !_compilation.IsSymbolAccessibleWithin(getter, _context))
            return false;

        if (!IsCompilerWrittenGetter(getter))
            return false;

        // A member of the same name between the owner and the declaring type would capture `owner.Name`.
        for (var current = owner as INamedTypeSymbol; current is not null && !SymbolEqualityComparer.Default.Equals(current, declaring); current = current.BaseType)
            if (current.GetMembers(property.Name).Length > 0)
                return false;

        return true;
    }

    /// <summary>
    /// The compiler's own getter: <c>get;</c> in source, a positional record property's synthesized accessor, or a
    /// metadata accessor marked [CompilerGenerated]. A getter with a body, the <c>field</c> keyword's included, is the user's.
    /// </summary>
    private static bool IsCompilerWrittenGetter(IMethodSymbol getter)
    {
        var references = getter.DeclaringSyntaxReferences;
        if (references.Length == 0)
            return getter.IsImplicitlyDeclared || getter.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == KnownTypes.CompilerGeneratedAttribute);

        foreach (var reference in references)
        {
            switch (reference.GetSyntax())
            {
                case Microsoft.CodeAnalysis.CSharp.Syntax.AccessorDeclarationSyntax { Body: null, ExpressionBody: null }:
                case Microsoft.CodeAnalysis.CSharp.Syntax.ParameterSyntax:
                    continue;
                default:
                    return false;
            }
        }

        return true;
    }

    /// <summary>True when the type or any containing type declares type parameters.</summary>
    public static bool IsGenericContext(INamedTypeSymbol type)
    {
        for (var current = type; current is not null; current = current.ContainingType)
            if (current.TypeParameters.Length > 0) 
                return true;

        return false;
    }

    private bool ConstraintsNameable(INamedTypeSymbol declaring)
    {
        for (var current = declaring.OriginalDefinition; current is not null; current = current.ContainingType)
            if (current.TypeParameters.Any(
                    parameter => parameter.ConstraintTypes.Any(
                        constraint => constraint is not ITypeParameterSymbol && !IsNameable(constraint))))
                return false;

        return true;
    }

    private static MemberCost CostOf(ClosureType type) => type.Kind switch
    {
        TypeKind.Leaf when type.LeafRule is LeafRule.Primitive or LeafRule.Enum or LeafRule.WideInteger or LeafRule.Single or LeafRule.Double or LeafRule.Half => 
            MemberCost.Primitive,
        TypeKind.Leaf when type.LeafRule == LeafRule.String => MemberCost.String,
        TypeKind.Leaf => MemberCost.SimpleLeaf,
        TypeKind.Struct => MemberCost.Struct,
        TypeKind.Nullable => MemberCost.Nullable,
        TypeKind.KeyValuePair or TypeKind.ValueTuple or TypeKind.Tuple => MemberCost.Product,
        TypeKind.Class or TypeKind.Dispatch => MemberCost.Reference,
        _ => MemberCost.Collection,
    };

    private static string StorageName(IFieldSymbol field)
    {
        if (field.AssociatedSymbol is IPropertySymbol property)
            return property.Name;

        var name = field.Name;
        if (name.Length <= 2 || name[0] != '<') 
            return name;
        
        var close = name.IndexOf('>');
        if (close <= 1) 
            return name;
        var suffix = name[(close + 1)..];
        return suffix is "k__BackingField" or "P" 
            ? name[1..close] 
            : name;
    }

    private bool HasIgnore(ISymbol symbol) => 
        _ignoreAttribute is not null && 
        symbol.GetAttributes().Any(a => 
            SymbolEqualityComparer.Default.Equals(a.AttributeClass, _ignoreAttribute) && a.ConstructorArguments.Length == 0);

    private static bool HasBackingField(INamedTypeSymbol declaring, IPropertySymbol property) => 
        declaring.GetMembers()
            .OfType<IFieldSymbol>()
            .Any(f => SymbolEqualityComparer.Default.Equals(f.AssociatedSymbol, property) ||
                      f.Name == $"<{property.Name}>k__BackingField");

    private HashSet<string> IgnoredPrimaryParameters(INamedTypeSymbol declaring)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        foreach (var constructor in declaring.InstanceConstructors)
            foreach (var parameter in constructor.Parameters)
                if (HasIgnore(parameter)) 
                    result.Add(parameter.Name);

        return result;
    }

    private bool IsContextIgnored(INamedTypeSymbol declaring, IFieldSymbol field, string name)
    {
        var ignored = false;
        foreach (var ignore in _registrations.ContextIgnores)
        {
            if (!SymbolEqualityComparer.Default.Equals(ignore.DeclaringType, declaring.OriginalDefinition) && 
                !SymbolEqualityComparer.Default.Equals(ignore.DeclaringType, declaring))
                continue;

            if (ignore.MemberName != field.Name && ignore.MemberName != name) 
                continue;
            
            ignore.Matched = true;
            ignored = true;
        }

        return ignored;
    }

    private void ReportUnmatchedContextIgnores()
    {
        foreach (var ignore in _registrations.ContextIgnores)
            if (!ignore.Matched) 
                _diagnostics.Add(DiagnosticInfo.Create(
                    Diagnostics.ContextIgnoreMatchedNothing, ignore.Location, ignore.DeclaringType.ToDisplayString(), ignore.MemberName));
    }

    private static bool ContainsDynamic(ITypeSymbol type) => type switch
    {
        IDynamicTypeSymbol => true,
        IArrayTypeSymbol array => ContainsDynamic(array.ElementType),
        INamedTypeSymbol named => named.TypeArguments.Any(ContainsDynamic),
        _ => false,
    };

    private bool IsInlineArray(ITypeSymbol type) =>
        _inlineArrayAttribute is not null && 
        type.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, _inlineArrayAttribute));

    private bool IsNameable(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => IsNameable(array.ElementType),
        INamedTypeSymbol named => _compilation.IsSymbolAccessibleWithin(named, _context) && named.TypeArguments.All(IsNameable),
        _ => _compilation.IsSymbolAccessibleWithin(type, _context),
    };

    private static bool ShouldWarnLazy(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named) 
            return false;

        if (named.TypeKind == RoslynTypeKind.Interface) 
            return true;

        var metadata = BuiltInLeaves.FullMetadataName(named.OriginalDefinition);
        return metadata is 
            "System.Collections.Generic.Queue`1" or 
            "System.Collections.Generic.Stack`1" or 
            "System.Collections.Generic.LinkedList`1" or 
            "System.Collections.ObjectModel.ReadOnlyCollection`1" or 
            "System.Collections.Immutable.ImmutableList`1" or 
            "System.Collections.Immutable.ImmutableQueue`1" or 
            "System.Collections.Immutable.ImmutableStack`1";
    }

    // ----- upward crawl -----------------------------------------------------------------------------------------------

    private void Crawl(ClosureType type, string path)
    {
        var named = (INamedTypeSymbol)type.Symbol;
        for (var current = named.BaseType; current is not null; current = current.BaseType)
        {
            if (IsStructuralBase(current)) 
                continue;

            Child(current, $"{path} : {current.Name}");
        }

        foreach (var iface in named.AllInterfaces)
        {
            if (IsExcludedInterface(iface))
                continue;

            Child(iface, $"{path} : {iface.Name}");
        }
    }

    private static bool IsStructuralBase(INamedTypeSymbol type) => type.SpecialType is 
        SpecialType.System_ValueType or SpecialType.System_Enum or SpecialType.System_Array or 
        SpecialType.System_Delegate or SpecialType.System_MulticastDelegate;

    private bool IsExcludedInterface(INamedTypeSymbol iface)
    {
        // An interface with an unimplemented static abstract member can never be an IEqualityComparer<T> argument (CS8920).
        if (TypeArgumentRules.IsConstraintOnlyInterface(iface)) 
            return true;

        var ns = iface.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal)) 
            return true;

        return _options.ExcludeInterfacesByPrefix
            .Any(prefix => ns == prefix || ns.StartsWith($"{prefix}.", StringComparison.Ordinal));
    }

    // ----- built-in admission and dispatch cases -----------------------------------------------------------------------

    private void AdmitBuiltInLeaves()
    {
        foreach (var entry in BuiltInLeaves.All)
        {
            var symbol = CapabilityProbe.Find(_compilation, entry.MetadataName);
            if (symbol is null || _types.ContainsKey(symbol) || FindCustom(symbol) is not null)
                continue;

            var admitted = WithEquatable(new ClosureType(symbol, TypeKind.Leaf)
            {
                LeafRule = entry.Rule,
                DefaultCompatible = entry.DefaultCompatible,
                Width = entry.Width,
                AggregateComponents = entry.Components,
                Reached = false,
            });
            _types.Add(symbol, admitted);
            _ordered.Add(admitted);
        }
    }

    private void ComputeDispatchCases()
    {
        foreach (var dispatch in _ordered.Where(t => t.IsDispatchCapable).ToList())
        {
            var assignable = new List<ClosureType>(_ordered.Count);
            var exact = new List<ClosureType>(_ordered.Count);
            foreach (var candidate in _ordered)
            {
                if (ReferenceEquals(candidate, dispatch) || candidate.Kind == TypeKind.Nullable) 
                    continue;

                if (!_compilation.IsAssignable(candidate.Symbol, dispatch.Symbol)) 
                    continue;

                var isDispatchOnly = candidate.Kind == TypeKind.Dispatch;
                if (isDispatchOnly)
                    continue;   // an abstract case can never be the exact runtime type

                if (candidate.Symbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
                    continue;   // boxing erases nullable origin: a box holds S or is null, never Nullable<S>

                if (candidate.Kind == TypeKind.Leaf)
                {
                    if (candidate.Symbol.IsValueType)
                        exact.Add(candidate);       // boxed value leaves: exact type test, then unbox
                    else
                        assignable.Add(candidate);  // reference leaves, user simple and custom rules accept derived runtime types
                }
                else if (candidate.IsContainer)
                {
                    if (candidate.IsCanonicalCase)
                        assignable.Add(candidate);   // int[] and List<int> both select IEnumerable<int>; HashSet<T> and SortedSet<T> both select ISet<T>
                    else if (candidate.IsValueShape && !ImplementsAnyCanonical(candidate))
                        exact.Add(candidate);        // a value container behind a dispatch type without a canonical case of its own
                }
                else if (candidate.Kind == TypeKind.Tuple) 
                    exact.Add(candidate);
                else 
                    exact.Add(candidate);
            }

            if (dispatch.Kind == TypeKind.Class || dispatch.Symbol.SpecialType == SpecialType.System_Object)
                exact.Add(dispatch);                 // the exact-self case of an unsealed concrete class, or of plain object (zero members)

            // Assignable cases: topological by assignability so a narrower rule precedes a broader one, then shape precedence,
            // then rule category, then name. Exact cases: by name; every runtime type matches at most one.
            assignable.Sort(CompareAssignable);
            assignable = TopologicalByAssignability(assignable);
            exact.Sort((a, b) => string.CompareOrdinal(a.Symbol.ToDisplayString(), b.Symbol.ToDisplayString()));

                        // A sealed assignable case whose type converts to no earlier case is the first match for exactly its own
            // runtime type, so its test can run before everything else and still select what the chain would.
            for (var i = 0; i < assignable.Count; i++)
            {
                var a = assignable[i];
                var hoistable = (a.Symbol.IsSealed || a.Symbol.IsValueType) &&
                                !assignable.Take(i).Any(b => _compilation.IsAssignable(a.Symbol, b.Symbol));
                dispatch.Cases.Add((a, false, -1, hoistable));
            }

            foreach (var e in exact)
            {
                var resolved = assignable.FindIndex(a => _compilation.IsAssignable(e.Symbol, a.Symbol));
                                dispatch.Cases.Add((e, true, resolved, false));
            }

            if (dispatch is { Kind: TypeKind.Dispatch, Cases.Count: 0 } &&
                dispatch.Symbol.SpecialType != SpecialType.System_Object)
                _diagnostics.Add(DiagnosticInfo.Create(
                    Diagnostics.NoDispatchCases, LocationInfo.From(dispatch.Symbol), dispatch.Symbol.ToDisplayString()));
        }
    }

    private bool ImplementsAnyCanonical(ClosureType candidate) => 
        _ordered.Any(t => t.IsCanonicalCase && _compilation.IsAssignable(candidate.Symbol, t.Symbol));

    private static int CompareAssignable(ClosureType a, ClosureType b)
    {
        var shape = ShapeRank(a).CompareTo(ShapeRank(b));
        if (shape != 0)
            return shape;

        var category = CategoryRank(a).CompareTo(CategoryRank(b));
        return category != 0
            ? category :
            string.CompareOrdinal(a.Symbol.ToDisplayString(), b.Symbol.ToDisplayString());
    }

    private static int ShapeRank(ClosureType type) => type.Kind switch
    {
        TypeKind.Dictionary => 0,
        TypeKind.Set => 1,
        TypeKind.Array or TypeKind.List => 2,
        TypeKind.ListInterface => 3,
        TypeKind.EnumerableInterface => 4,
        _ => 5,
    };

    private static int CategoryRank(ClosureType type) => type.LeafRule switch
    {
        LeafRule.Custom => 0,
        LeafRule.UserSimple => 1,
        _ => 2,
    };

    private List<ClosureType> TopologicalByAssignability(List<ClosureType> cases)
    {
        // Stable insertion: place each case before the first existing case it can be assigned to.
        var result = new List<ClosureType>(cases.Count);
        foreach (var candidate in cases)
        {
            var insertAt = result.Count;
            for (var i = 0; i < result.Count; i++)
            {
                if (!_compilation.IsAssignable(candidate.Symbol, result[i].Symbol) ||
                    SymbolEqualityComparer.Default.Equals(candidate.Symbol, result[i].Symbol)) 
                    continue;
                insertAt = i;
                break;
            }

            result.Insert(insertAt, candidate);
        }

        return result;
    }
}
