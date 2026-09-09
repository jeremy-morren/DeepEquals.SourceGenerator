using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DeepEquals.SourceGenerator.Model;
using Microsoft.CodeAnalysis;
using TypeKind = DeepEquals.SourceGenerator.Model.TypeKind;
using RoslynTypeKind = Microsoft.CodeAnalysis.TypeKind;

namespace DeepEquals.SourceGenerator.Analysis;

/// <summary>A closure type at the symbol level, before naming and graph analysis freeze it into a <see cref="TypeModel"/>.</summary>
internal sealed class ClosureType
{
    public ClosureType(ITypeSymbol symbol, TypeKind kind)
    {
        Symbol = symbol;
        Kind = kind;
    }

    public ITypeSymbol Symbol { get; }

    public TypeKind Kind { get; set; }

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

    public AggregateComponent[] AggregateComponents { get; set; } = Array.Empty<AggregateComponent>();

    public CustomRegistration? Custom { get; set; }

    /// <summary>The custom registration targets Nullable of this value type; values are wrapped for the comparer.</summary>
    public bool CustomWrapsNullable { get; set; }

    public ClosureType? Payload { get; set; }

    public ClosureType? Element { get; set; }

    public ClosureType? Key { get; set; }

    public ClosureType? Value { get; set; }

    public List<ClosureType> Items { get; } = new List<ClosureType>();

    public bool IsReadOnlyMemory { get; set; }

    public INamedTypeSymbol? CollectionInterface { get; set; }

    public List<ClosureMember> Members { get; } = new List<ClosureMember>();

    public bool HasStorageIgnoredByShape { get; set; }

    /// <summary>Dispatch cases in emission order: assignable first, exact last.</summary>
    public List<(ClosureType Type, bool IsExact)> Cases { get; } = new List<(ClosureType, bool)>();

    public bool IsDispatchCapable => Kind == TypeKind.Dispatch || (Kind == TypeKind.Class && !Symbol.IsSealed);

    public bool IsDeep => Kind == TypeKind.Class || Kind == TypeKind.Struct;

    public bool IsValueShape => Kind is TypeKind.Struct or TypeKind.KeyValuePair or TypeKind.ValueTuple or TypeKind.Memory or TypeKind.ImmutableArray or TypeKind.ArraySegment;

    public bool IsContainer => Kind is TypeKind.Array or TypeKind.List or TypeKind.ImmutableArray or TypeKind.ArraySegment or TypeKind.Memory
        or TypeKind.ListInterface or TypeKind.EnumerableInterface or TypeKind.Set or TypeKind.Dictionary;

    public bool IsProduct => Kind is TypeKind.KeyValuePair or TypeKind.ValueTuple or TypeKind.Tuple;

    public bool IsUnordered => Kind is TypeKind.Set or TypeKind.Dictionary;
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
    public ClosureResult(List<ClosureType> types, ClosureType objectType, Registrations registrations, bool failed)
    {
        Types = types;
        ObjectType = objectType;
        Registrations = registrations;
        Failed = failed;
    }

    public List<ClosureType> Types { get; }

    public ClosureType ObjectType { get; }

    public Registrations Registrations { get; }

    public bool Failed { get; }
}

/// <summary>Builds the closure of the registered roots: classification, member selection, the upward crawl and dispatch cases.</summary>
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
    private readonly Dictionary<ITypeSymbol, ClosureType> _types = new Dictionary<ITypeSymbol, ClosureType>(SymbolEqualityComparer.Default);
    private readonly List<ClosureType> _ordered = new List<ClosureType>();
    private readonly Queue<(ClosureType Type, string Path)> _work = new Queue<(ClosureType, string)>();
    private readonly INamedTypeSymbol? _referenceAssemblyAttribute;
    private readonly INamedTypeSymbol? _ignoreAttribute;
    private readonly INamedTypeSymbol? _inlineArrayAttribute;
    private readonly INamedTypeSymbol? _compilerGeneratedAttribute;
    private readonly INamedTypeSymbol? _immutableArray;
    private readonly INamedTypeSymbol? _iReadOnlySet;
    private readonly INamedTypeSymbol? _memory;
    private readonly INamedTypeSymbol? _readOnlyMemory;
    private readonly INamedTypeSymbol? _arraySegment;
    private readonly INamedTypeSymbol? _tupleBase;
    private bool _failed;
    private bool _regexWarned;

    public ClosureBuilder(Compilation compilation, INamedTypeSymbol context, ContextOptions options, TargetCapabilities capabilities, Registrations registrations, List<DiagnosticInfo> diagnostics, CancellationToken cancellationToken)
    {
        _compilation = compilation;
        _context = context;
        _options = options;
        _capabilities = capabilities;
        _registrations = registrations;
        _diagnostics = diagnostics;
        _cancellationToken = cancellationToken;
        _referenceAssemblyAttribute = CapabilityProbe.Find(compilation, KnownTypes.ReferenceAssemblyAttribute);
        _ignoreAttribute = CapabilityProbe.Find(compilation, KnownTypes.IgnoreAttribute);
        _inlineArrayAttribute = CapabilityProbe.Find(compilation, KnownTypes.InlineArrayAttribute);
        _compilerGeneratedAttribute = CapabilityProbe.Find(compilation, KnownTypes.CompilerGeneratedAttribute);
        _immutableArray = CapabilityProbe.Find(compilation, KnownTypes.ImmutableArray);
        _iReadOnlySet = CapabilityProbe.Find(compilation, KnownTypes.IReadOnlySet);
        _memory = CapabilityProbe.Find(compilation, KnownTypes.Memory);
        _readOnlyMemory = CapabilityProbe.Find(compilation, KnownTypes.ReadOnlyMemory);
        _arraySegment = CapabilityProbe.Find(compilation, "System.ArraySegment`1");
        _tupleBase = CapabilityProbe.Find(compilation, "System.Tuple`1");
    }

    public ClosureResult Build()
    {
        // object is the closed-world root of every context.
        ClosureType objectType = Get(_compilation.GetSpecialType(SpecialType.System_Object), "object");
        objectType.Reached = true;

        foreach ((INamedTypeSymbol type, LocationInfo? location) in _registrations.Roots)
        {
            ClosureType root = Get(type, type.ToDisplayString());
            root.Reached = true;
            root.IsRoot = true;
        }

        foreach ((ITypeSymbol type, LocationInfo? location) in _registrations.SimpleTypes)
        {
            ClosureType simple = Get(type, type.ToDisplayString());
            simple.Reached = true;
        }

        foreach (CustomRegistration custom in _registrations.Custom)
        {
            if (custom.Ignored)
            {
                continue;
            }

            ClosureType covered = Get(custom.Target, custom.Target.ToDisplayString());
            covered.Reached = true;
        }

        while (_work.Count > 0 && !_failed)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            (ClosureType type, string path) = _work.Dequeue();
            Expand(type, path);
        }

        if (_failed)
        {
            return new ClosureResult(_ordered, objectType, _registrations, failed: true);
        }

        AdmitBuiltInLeaves();
        AddCanonicalContainerInterfaces();
        while (_work.Count > 0 && !_failed)
        {
            _cancellationToken.ThrowIfCancellationRequested();
            (ClosureType type, string path) = _work.Dequeue();
            Expand(type, path);
        }

        if (_failed)
        {
            return new ClosureResult(_ordered, objectType, _registrations, failed: true);
        }

        ComputeDispatchCases();
        ReportUnmatchedContextIgnores();
        return new ClosureResult(_ordered, objectType, _registrations, failed: false);
    }

    /// <summary>
    /// Behind a dispatch type, container cases are canonical per family and type arguments: IEnumerable&lt;T&gt; for every
    /// ordered container of T, ISet&lt;T&gt; (and IReadOnlySet&lt;T&gt; where it exists) for sets, IReadOnlyDictionary and IDictionary
    /// for dictionaries. The interface closure types those cases route to are created here so that an int[] and a List&lt;int&gt;
    /// behind object share one core and one hash formula.
    /// </summary>
    private void AddCanonicalContainerInterfaces()
    {
        INamedTypeSymbol enumerable = _compilation.GetSpecialType(SpecialType.System_Collections_Generic_IEnumerable_T);
        INamedTypeSymbol? set = CapabilityProbe.Find(_compilation, "System.Collections.Generic.ISet`1");
        INamedTypeSymbol? readOnlySet = _iReadOnlySet;
        INamedTypeSymbol? dictionary = CapabilityProbe.Find(_compilation, "System.Collections.Generic.IDictionary`2");
        INamedTypeSymbol? readOnlyDictionary = CapabilityProbe.Find(_compilation, "System.Collections.Generic.IReadOnlyDictionary`2");

        foreach (ClosureType container in _ordered.Where(t => t.IsContainer && t.Reached).ToList())
        {
            switch (container.Kind)
            {
                case TypeKind.Set:
                    if (set is not null) Canonical(set.Construct(container.Element!.Symbol));
                    if (readOnlySet is not null) Canonical(readOnlySet.Construct(container.Element!.Symbol));
                    break;
                case TypeKind.Dictionary:
                    if (readOnlyDictionary is not null) Canonical(readOnlyDictionary.Construct(container.Key!.Symbol, container.Value!.Symbol));
                    if (dictionary is not null) Canonical(dictionary.Construct(container.Key!.Symbol, container.Value!.Symbol));
                    break;
                default:
                    Canonical(enumerable.Construct(container.Element!.Symbol));
                    break;
            }
        }
    }

    private void Canonical(INamedTypeSymbol iface)
    {
        ClosureType type = Get(iface, iface.ToDisplayString());
        type.Reached = true;
        type.IsCanonicalCase = true;
    }

    // ----- lookup -------------------------------------------------------------------------------------------------------

    private ClosureType Get(ITypeSymbol symbol, string path)
    {
        symbol = symbol.WithNullableAnnotation(NullableAnnotation.None);
        if (_types.TryGetValue(symbol, out ClosureType? existing))
        {
            return existing;
        }

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

        ClosureType created = Classify(symbol);
        _types.Add(symbol, created);
        _ordered.Add(created);
        _work.Enqueue((created, path));
        return created;
    }

    private static int GenericDepth(ITypeSymbol symbol)
    {
        switch (symbol)
        {
            case IArrayTypeSymbol array:
                return 1 + GenericDepth(array.ElementType);
            case INamedTypeSymbol named when named.IsGenericType:
                int max = 0;
                foreach (ITypeSymbol argument in named.TypeArguments)
                {
                    max = Math.Max(max, GenericDepth(argument));
                }

                return 1 + max;
            default:
                return 0;
        }
    }

    private void Fail(DiagnosticDescriptor descriptor, LocationInfo? location, params object?[] args)
    {
        _diagnostics.Add(DiagnosticInfo.Create(descriptor, location, args));
        if (descriptor.DefaultSeverity == DiagnosticSeverity.Error)
        {
            _failed = true;
        }
    }

    // ----- classification ---------------------------------------------------------------------------------------------

    private ClosureType Classify(ITypeSymbol symbol)
    {
        // Custom comparers first: an exact registration, then a covering reference or interface registration.
        CustomRegistration? custom = FindCustom(symbol, out bool wrapsNullable);
        if (custom is not null)
        {
            if (IsUserSimple(symbol))
            {
                _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.StrategyOverlap, custom.Location, $"'{symbol.ToDisplayString()}' is covered by both a [SimpleType] rule and the [CustomEqualityComparer] for '{custom.Target.ToDisplayString()}'; the custom comparer wins"));
            }

            return new ClosureType(symbol, TypeKind.Leaf) { LeafRule = LeafRule.Custom, Custom = custom, CustomWrapsNullable = wrapsNullable, DefaultCompatible = false };
        }

        BuiltInLeaves.Entry? builtIn = BuiltInLeaves.Find(symbol);
        if (builtIn is not null)
        {
            return WithEquatable(new ClosureType(symbol, TypeKind.Leaf)
            {
                LeafRule = builtIn.Rule,
                DefaultCompatible = builtIn.DefaultCompatible,
                Width = builtIn.Width,
                AggregateComponents = builtIn.Components,
            });
        }

        if (symbol.TypeKind == RoslynTypeKind.Enum)
        {
            return new ClosureType(symbol, TypeKind.Leaf) { LeafRule = LeafRule.Enum, DefaultCompatible = true, Width = EnumWidth(symbol) };
        }

        if (symbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable)
        {
            return new ClosureType(symbol, TypeKind.Nullable) { Payload = null };
        }

        if (IsUserSimple(symbol))
        {
            return WithEquatable(new ClosureType(symbol, TypeKind.Leaf) { LeafRule = LeafRule.UserSimple, DefaultCompatible = true });
        }

        if (symbol.SpecialType == SpecialType.System_Object)
        {
            return new ClosureType(symbol, TypeKind.Dispatch);
        }

        if (symbol is IArrayTypeSymbol array)
        {
            return new ClosureType(symbol, TypeKind.Array);
        }

        if (symbol is INamedTypeSymbol named)
        {
            TypeKind? shape = ClassifyShape(named, out INamedTypeSymbol? collectionInterface, out bool isReadOnlyMemory);
            if (shape is not null)
            {
                return new ClosureType(symbol, shape.Value) { CollectionInterface = collectionInterface, IsReadOnlyMemory = isReadOnlyMemory };
            }

            if (named.TypeKind == RoslynTypeKind.Interface || named.IsAbstract)
            {
                return new ClosureType(symbol, TypeKind.Dispatch);
            }

            return new ClosureType(symbol, named.IsValueType ? TypeKind.Struct : TypeKind.Class);
        }

        return new ClosureType(symbol, TypeKind.Dispatch);
    }

    private CustomRegistration? FindCustom(ITypeSymbol symbol) => FindCustom(symbol, out _);

    private CustomRegistration? FindCustom(ITypeSymbol symbol, out bool wrapsNullable)
    {
        wrapsNullable = false;
        CustomRegistration? exact = null;
        CustomRegistration? covering = null;
        CustomRegistration? second = null;
        CustomRegistration? nullableOfSymbol = null;
        foreach (CustomRegistration registration in _registrations.Custom)
        {
            if (registration.Ignored)
            {
                continue;
            }

            if (SymbolEqualityComparer.Default.Equals(registration.Target, symbol))
            {
                exact = registration;
                break;
            }

            if (registration.Target.IsValueType)
            {
                // Value-type targets are exact only, except that a registration for S? also serves a non-nullable S by wrapping.
                if (symbol.IsValueType && registration.Target is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullableTarget
                    && SymbolEqualityComparer.Default.Equals(nullableTarget.TypeArguments[0], symbol))
                {
                    nullableOfSymbol = registration;
                }

                continue;
            }

            if (_compilation.IsAssignable(symbol, registration.Target) && !(symbol.IsValueType && registration.Target.SpecialType == SpecialType.System_Object))
            {
                if (covering is null)
                {
                    covering = registration;
                }
                else if (_compilation.IsAssignable(covering.Target, registration.Target))
                {
                    // covering is narrower; keep it
                }
                else if (_compilation.IsAssignable(registration.Target, covering.Target))
                {
                    covering = registration;
                }
                else
                {
                    second = registration;
                }
            }
        }

        if (exact is not null)
        {
            return exact;
        }

        if (nullableOfSymbol is not null)
        {
            wrapsNullable = true;
            return nullableOfSymbol;
        }

        if (covering is not null && second is not null)
        {
            Fail(Diagnostics.AmbiguousCustomInterfaceComparers, covering.Location, symbol.ToDisplayString(), covering.Target.ToDisplayString(), second.Target.ToDisplayString());
        }

        return covering;
    }

    private bool IsUserSimple(ITypeSymbol symbol)
    {
        foreach ((ITypeSymbol simple, LocationInfo? _) in _registrations.SimpleTypes)
        {
            if (SymbolEqualityComparer.Default.Equals(simple, symbol) || (!symbol.IsValueType || simple.TypeKind == RoslynTypeKind.Interface || simple.TypeKind == RoslynTypeKind.Class) && _compilation.IsAssignable(symbol, simple) && simple.SpecialType != SpecialType.System_Object)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Records whether the leaf implements <c>IEquatable&lt;Self&gt;</c> exactly and, if so, whether that implementation is a
    /// public <c>Equals(Self)</c>. Only the exact self instantiation counts: a <c>Derived</c> inheriting <c>IEquatable&lt;Base&gt;</c>
    /// keeps <c>EqualityComparer&lt;Derived&gt;.Default</c>, whose interface dispatch it cannot be proved to match statically.
    /// </summary>
    private static ClosureType WithEquatable(ClosureType type)
    {
        ITypeSymbol symbol = type.Symbol;
        foreach (INamedTypeSymbol iface in symbol.AllInterfaces)
        {
            if (!iface.IsGenericType || BuiltInLeaves.FullMetadataName(iface.OriginalDefinition) != "System.IEquatable`1" || !SymbolEqualityComparer.Default.Equals(iface.TypeArguments[0], symbol))
            {
                continue;
            }

            type.ImplementsIEquatable = true;
            IMethodSymbol? interfaceEquals = iface.GetMembers("Equals").OfType<IMethodSymbol>().FirstOrDefault(m => m.Parameters.Length == 1);
            ISymbol? implementation = interfaceEquals is null ? null : symbol.FindImplementationForInterfaceMember(interfaceEquals);
            type.HasPublicEquatableEquals = implementation is IMethodSymbol
            {
                DeclaredAccessibility: Accessibility.Public,
                IsStatic: false,
                MethodKind: MethodKind.Ordinary,
                Name: "Equals",
                Parameters.Length: 1,
            } method
                && method.Parameters[0].RefKind == RefKind.None
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

        INamedTypeSymbol? dictionary = FindFamily(named, SpecialType.None, "System.Collections.Generic.IReadOnlyDictionary`2", "System.Collections.Generic.IDictionary`2");
        if (dictionary is not null)
        {
            collectionInterface = dictionary;
            return TypeKind.Dictionary;
        }

        INamedTypeSymbol? set = FindFamily(named, SpecialType.None, "System.Collections.Generic.IReadOnlySet`1", "System.Collections.Generic.ISet`1");
        if (set is not null)
        {
            collectionInterface = set;
            return TypeKind.Set;
        }

        INamedTypeSymbol definition = named.OriginalDefinition;
        string metadata = BuiltInLeaves.FullMetadataName(definition);
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

        INamedTypeSymbol? list = FindFamily(named, SpecialType.None, "System.Collections.Generic.IReadOnlyList`1", "System.Collections.Generic.IList`1");
        if (list is not null)
        {
            collectionInterface = list;
            return TypeKind.ListInterface;
        }

        INamedTypeSymbol? enumerable = FindFamily(named, SpecialType.System_Collections_Generic_IEnumerable_T, "System.Collections.Generic.IReadOnlyCollection`1", "System.Collections.Generic.IEnumerable`1");
        if (enumerable is not null)
        {
            collectionInterface = enumerable;
            return TypeKind.EnumerableInterface;
        }

        return null;
    }

    /// <summary>The first instantiation of the family on the type itself or in AllInterfaces; DEQ015 when instantiations disagree.</summary>
    private INamedTypeSymbol? FindFamily(INamedTypeSymbol named, SpecialType special, params string[] metadataNames)
    {
        List<INamedTypeSymbol> found = new List<INamedTypeSymbol>();
        void Consider(INamedTypeSymbol candidate)
        {
            if (candidate.TypeKind != RoslynTypeKind.Interface || !candidate.IsGenericType)
            {
                return;
            }

            string name = BuiltInLeaves.FullMetadataName(candidate.OriginalDefinition);
            foreach (string wanted in metadataNames)
            {
                if (string.Equals(name, wanted, StringComparison.Ordinal))
                {
                    found.Add(candidate);
                    return;
                }
            }
        }

        Consider(named);
        foreach (INamedTypeSymbol iface in named.AllInterfaces)
        {
            Consider(iface);
        }

        if (found.Count == 0)
        {
            return null;
        }

        // Prefer the most specific family member (first metadata name), then AllInterfaces order.
        INamedTypeSymbol chosen = found[0];
        foreach (string wanted in metadataNames)
        {
            INamedTypeSymbol? match = found.FirstOrDefault(f => string.Equals(BuiltInLeaves.FullMetadataName(f.OriginalDefinition), wanted, StringComparison.Ordinal));
            if (match is not null)
            {
                chosen = match;
                break;
            }
        }

        // Instantiations of the winning family must agree on their type arguments.
        List<INamedTypeSymbol> distinct = found
            .Where(f => !f.TypeArguments.Zip(chosen.TypeArguments, (a, b) => SymbolEqualityComparer.Default.Equals(a, b)).All(equal => equal))
            .ToList();
        if (distinct.Count > 0)
        {
            _diagnostics.Add(DiagnosticInfo.Create(
                Diagnostics.AmbiguousCollectionShape,
                LocationInfo.From(named),
                named.ToDisplayString(),
                metadataNames[metadataNames.Length - 1],
                string.Join(", ", found.Select(f => f.ToDisplayString()).Distinct()),
                chosen.ToDisplayString()));
        }

        return chosen;
    }

    // ----- expansion --------------------------------------------------------------------------------------------------

    private void Expand(ClosureType type, string path)
    {
        ITypeSymbol symbol = type.Symbol;
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
                type.Payload = Child(((INamedTypeSymbol)symbol).TypeArguments[0], path + "?");
                break;

            case TypeKind.Array:
                IArrayTypeSymbol array = (IArrayTypeSymbol)symbol;
                if (array.Rank != 1)
                {
                    Fail(Diagnostics.MultiDimensionalArray, null, path, "?", symbol.ToDisplayString());
                    return;
                }

                type.Element = Child(array.ElementType, path + "[]");
                break;

            case TypeKind.List:
            case TypeKind.ImmutableArray:
            case TypeKind.ArraySegment:
            case TypeKind.Memory:
                type.Element = Child(((INamedTypeSymbol)symbol).TypeArguments[0], path + "<>");
                break;

            case TypeKind.ListInterface:
            case TypeKind.EnumerableInterface:
            case TypeKind.Set:
                type.Element = Child(type.CollectionInterface!.TypeArguments[0], path + "<>");
                WarnShapeIgnoresStorage(type);
                break;

            case TypeKind.Dictionary:
                type.Key = Child(type.CollectionInterface!.TypeArguments[0], path + "<key>");
                type.Value = Child(type.CollectionInterface!.TypeArguments[1], path + "<value>");
                WarnShapeIgnoresStorage(type);
                break;

            case TypeKind.KeyValuePair:
                type.Key = Child(((INamedTypeSymbol)symbol).TypeArguments[0], path + ".Key");
                type.Value = Child(((INamedTypeSymbol)symbol).TypeArguments[1], path + ".Value");
                break;

            case TypeKind.ValueTuple:
            case TypeKind.Tuple:
                foreach (ITypeSymbol item in FlattenTuple((INamedTypeSymbol)symbol, type.Kind == TypeKind.ValueTuple))
                {
                    type.Items.Add(Child(item, path + ".Item"));
                }

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
        ClosureType child = Get(symbol, path);
        child.Reached = true;
        return child;
    }

    private void WarnShapeIgnoresStorage(ClosureType type)
    {
        if (type.Symbol is not INamedTypeSymbol named || named.TypeKind == RoslynTypeKind.Interface)
        {
            return;
        }

        if (IsFrameworkType(named))
        {
            return;
        }

        List<string> storage = new List<string>();
        for (INamedTypeSymbol? current = named; current is not null && current.SpecialType != SpecialType.System_Object; current = current.BaseType)
        {
            if (IsFrameworkType(current))
            {
                break;
            }

            foreach (IFieldSymbol field in current.GetMembers().OfType<IFieldSymbol>())
            {
                if (!field.IsStatic && !field.IsConst)
                {
                    storage.Add(field.AssociatedSymbol?.Name ?? field.Name);
                }
            }
        }

        if (storage.Count > 0)
        {
            type.HasStorageIgnoredByShape = true;
            _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.CollectionShapeIgnoresStorage, LocationInfo.From(named), named.ToDisplayString(), type.CollectionInterface!.ToDisplayString(), string.Join(", ", storage)));
        }
    }

    private static bool IsFrameworkType(INamedTypeSymbol type)
    {
        string ns = type.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        return ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal);
    }

    private static IEnumerable<ITypeSymbol> FlattenTuple(INamedTypeSymbol tuple, bool valueTuple)
    {
        for (int i = 0; i < tuple.TypeArguments.Length; i++)
        {
            ITypeSymbol argument = tuple.TypeArguments[i];
            if (i == 7 && argument is INamedTypeSymbol rest && rest.IsGenericType
                && BuiltInLeaves.FullMetadataName(rest.OriginalDefinition).StartsWith(valueTuple ? "System.ValueTuple`" : "System.Tuple`", StringComparison.Ordinal))
            {
                foreach (ITypeSymbol nested in FlattenTuple(rest, valueTuple))
                {
                    yield return nested;
                }
            }
            else
            {
                yield return argument;
            }
        }
    }

    // ----- member selection -------------------------------------------------------------------------------------------

    private void SelectMembers(ClosureType type, string path)
    {
        INamedTypeSymbol named = (INamedTypeSymbol)type.Symbol;
        bool fromReferenceAssembly = _referenceAssemblyAttribute is not null
            && named.ContainingAssembly.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, _referenceAssemblyAttribute));

        // Bases first, then the type; skip object.
        List<INamedTypeSymbol> chain = new List<INamedTypeSymbol>();
        for (INamedTypeSymbol? current = named; current is not null && current.SpecialType != SpecialType.System_Object && current.SpecialType != SpecialType.System_ValueType; current = current.BaseType)
        {
            chain.Add(current);
        }

        chain.Reverse();

        if (fromReferenceAssembly)
        {
            if (type.Kind == TypeKind.Class)
            {
                Fail(Diagnostics.ReferenceAssemblyClass, LocationInfo.From(named), named.ToDisplayString());
                return;
            }

            bool placeholdersOnly = named.GetMembers().OfType<IFieldSymbol>().Where(f => !f.IsStatic).All(f => f.Name.StartsWith("_dummy", StringComparison.Ordinal));
            if (placeholdersOnly)
            {
                Fail(Diagnostics.ReferenceAssemblyClass, LocationInfo.From(named), named.ToDisplayString());
                return;
            }

            _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.ReferenceAssemblyStruct, LocationInfo.From(named), named.ToDisplayString()));
        }
        else if (IsFrameworkType(named))
        {
            _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.FrameworkTypeWalked, null, named.ToDisplayString()));
        }

        int order = 0;
        foreach (INamedTypeSymbol declaring in chain)
        {
            HashSet<string> ignoredParameters = IgnoredPrimaryParameters(declaring);
            foreach (string parameter in ignoredParameters)
            {
                if (!declaring.GetMembers("<" + parameter + ">P").Any())
                {
                    _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.IgnoreOnComputedMember, LocationInfo.From(declaring), parameter, declaring.ToDisplayString()));
                }
            }
            foreach (ISymbol member in declaring.GetMembers())
            {
                if (member is IPropertySymbol property && HasIgnore(property) && !HasBackingField(declaring, property))
                {
                    _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.IgnoreOnComputedMember, LocationInfo.From(property), property.Name, declaring.ToDisplayString()));
                    continue;
                }

                if (member is not IFieldSymbol field || field.IsStatic || field.IsConst)
                {
                    continue;
                }

                string name = StorageName(field);
                if (field.AssociatedSymbol is IEventSymbol || field.Type.TypeKind == RoslynTypeKind.Delegate)
                {
                    _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.DelegateOrEventSkipped, LocationInfo.From(field.AssociatedSymbol ?? field), name, declaring.ToDisplayString()));
                    continue;
                }

                if (HasIgnore(field) || (field.AssociatedSymbol is not null && HasIgnore(field.AssociatedSymbol)) || IsContextIgnored(declaring, field, name) || ignoredParameters.Contains(name))
                {
                    continue;
                }

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
                    Fail(Diagnostics.UnsupportedMemberType, LocationInfo.From(field.AssociatedSymbol ?? field), name, declaring.ToDisplayString(), field.Type.ToDisplayString(), "is a ref struct, pointer or ref field");
                    continue;
                }

                if (TypeArgumentRules.FindConstraintOnlyInterface(field.Type) is INamedTypeSymbol constraintOnly)
                {
                    string reason = SymbolEqualityComparer.Default.Equals(constraintOnly, field.Type)
                        ? "has a static abstract member without an implementation and cannot be an IEqualityComparer<T> type argument"
                        : $"contains '{constraintOnly.ToDisplayString()}', an interface with a static abstract member without an implementation that cannot be an IEqualityComparer<T> type argument";
                    Fail(Diagnostics.UnsupportedMemberType, LocationInfo.From(field.AssociatedSymbol ?? field), name, declaring.ToDisplayString(), field.Type.ToDisplayString(), reason);
                    continue;
                }

                if (field.Type is IArrayTypeSymbol { Rank: > 1 } && FindCustom(field.Type) is null)
                {
                    Fail(Diagnostics.MultiDimensionalArray, LocationInfo.From(field.AssociatedSymbol ?? field), name, declaring.ToDisplayString(), field.Type.ToDisplayString());
                    continue;
                }

                if (!IsNameable(field.Type))
                {
                    Fail(Diagnostics.InaccessibleMemberType, LocationInfo.From(field.AssociatedSymbol ?? field), name, declaring.ToDisplayString(), field.Type.ToDisplayString());
                    continue;
                }

                ClosureType memberType = Child(field.Type, path + "." + name);
                if (memberType.Kind == TypeKind.EnumerableInterface && ShouldWarnLazy(field.Type))
                {
                    _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.PossiblyLazyEnumerable, LocationInfo.From(field.AssociatedSymbol ?? field), name, declaring.ToDisplayString(), field.Type.ToDisplayString()));
                }

                // Compiler storage can never be read directly: C# cannot name <Name>k__BackingField even where the symbol is accessible.
                bool compilerStorage = field.IsImplicitlyDeclared || field.Name.StartsWith("<", StringComparison.Ordinal);
                bool genericDeclaring = IsGenericContext(declaring);
                MemberAccess access;
                if (!compilerStorage && _compilation.IsSymbolAccessibleWithin(field, _context))
                {
                    access = MemberAccess.Direct;
                }
                else if (!_capabilities.HasUnsafeAccessor)
                {
                    access = MemberAccess.Delegate;
                }
                else if (!genericDeclaring)
                {
                    access = MemberAccess.UnsafeAccessor;
                }
                else
                {
                    // .NET 9 matches a generic accessor by position and constraints; a constraint the context cannot name falls back.
                    access = _capabilities.HasGenericUnsafeAccessor && ConstraintsNameable(declaring) ? MemberAccess.UnsafeAccessor : MemberAccess.Delegate;
                }

                type.Members.Add(new ClosureMember(field, name, memberType, access, CostOf(memberType), order++));
            }
        }

        // Cheapest first, stable within a class.
        type.Members.Sort((a, b) =>
        {
            int cost = a.Cost.CompareTo(b.Cost);
            return cost != 0 ? cost : a.Order.CompareTo(b.Order);
        });
    }

    /// <summary>True when the type or any containing type declares type parameters.</summary>
    public static bool IsGenericContext(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.TypeParameters.Length > 0)
            {
                return true;
            }
        }

        return false;
    }

    private bool ConstraintsNameable(INamedTypeSymbol declaring)
    {
        for (INamedTypeSymbol? current = declaring.OriginalDefinition; current is not null; current = current.ContainingType)
        {
            foreach (ITypeParameterSymbol parameter in current.TypeParameters)
            {
                foreach (ITypeSymbol constraint in parameter.ConstraintTypes)
                {
                    if (constraint is not ITypeParameterSymbol && !IsNameable(constraint))
                    {
                        return false;
                    }
                }
            }
        }

        return true;
    }

    private static MemberCost CostOf(ClosureType type) => type.Kind switch
    {
        TypeKind.Leaf when type.LeafRule is LeafRule.Primitive or LeafRule.Enum or LeafRule.WideInteger or LeafRule.Single or LeafRule.Double or LeafRule.Half => MemberCost.Primitive,
        TypeKind.Leaf when type.LeafRule == LeafRule.String => MemberCost.String,
        TypeKind.Leaf => MemberCost.SimpleLeaf,
        TypeKind.Struct => MemberCost.Struct,
        TypeKind.Nullable => MemberCost.Nullable,
        TypeKind.KeyValuePair or TypeKind.ValueTuple or TypeKind.Tuple => MemberCost.Product,
        TypeKind.Class or TypeKind.Dispatch => MemberCost.Reference,
        _ => MemberCost.Collection,
    };

    private string StorageName(IFieldSymbol field)
    {
        if (field.AssociatedSymbol is IPropertySymbol property)
        {
            return property.Name;
        }

        string name = field.Name;
        if (name.Length > 2 && name[0] == '<')
        {
            int close = name.IndexOf('>');
            if (close > 1)
            {
                string suffix = name.Substring(close + 1);
                if (suffix == "k__BackingField" || suffix == "P")
                {
                    return name.Substring(1, close - 1);
                }
            }
        }

        return name;
    }

    private bool HasIgnore(ISymbol symbol)
        => _ignoreAttribute is not null && symbol.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, _ignoreAttribute) && a.ConstructorArguments.Length == 0);

    private bool HasBackingField(INamedTypeSymbol declaring, IPropertySymbol property)
        => declaring.GetMembers().OfType<IFieldSymbol>().Any(f => SymbolEqualityComparer.Default.Equals(f.AssociatedSymbol, property) || f.Name == "<" + property.Name + ">k__BackingField");

    private HashSet<string> IgnoredPrimaryParameters(INamedTypeSymbol declaring)
    {
        HashSet<string> result = new HashSet<string>(StringComparer.Ordinal);
        foreach (IMethodSymbol constructor in declaring.InstanceConstructors)
        {
            foreach (IParameterSymbol parameter in constructor.Parameters)
            {
                if (HasIgnore(parameter))
                {
                    result.Add(parameter.Name);
                }
            }
        }

        return result;
    }

    private bool IsContextIgnored(INamedTypeSymbol declaring, IFieldSymbol field, string name)
    {
        bool ignored = false;
        foreach (ContextIgnore ignore in _registrations.ContextIgnores)
        {
            if (!SymbolEqualityComparer.Default.Equals(ignore.DeclaringType, declaring.OriginalDefinition) && !SymbolEqualityComparer.Default.Equals(ignore.DeclaringType, declaring))
            {
                continue;
            }

            if (ignore.MemberName == field.Name || ignore.MemberName == name)
            {
                ignore.Matched = true;
                ignored = true;
            }
        }

        return ignored;
    }

    private void ReportUnmatchedContextIgnores()
    {
        foreach (ContextIgnore ignore in _registrations.ContextIgnores)
        {
            if (!ignore.Matched)
            {
                _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.ContextIgnoreMatchedNothing, ignore.Location, ignore.DeclaringType.ToDisplayString(), ignore.MemberName));
            }
        }
    }

    private static bool ContainsDynamic(ITypeSymbol type) => type switch
    {
        IDynamicTypeSymbol => true,
        IArrayTypeSymbol array => ContainsDynamic(array.ElementType),
        INamedTypeSymbol named => named.TypeArguments.Any(ContainsDynamic),
        _ => false,
    };

    private bool IsInlineArray(ITypeSymbol type)
        => _inlineArrayAttribute is not null && type.GetAttributes().Any(a => SymbolEqualityComparer.Default.Equals(a.AttributeClass, _inlineArrayAttribute));

    private bool IsNameable(ITypeSymbol type) => type switch
    {
        IArrayTypeSymbol array => IsNameable(array.ElementType),
        INamedTypeSymbol named => _compilation.IsSymbolAccessibleWithin(named, _context) && named.TypeArguments.All(IsNameable),
        _ => _compilation.IsSymbolAccessibleWithin(type, _context),
    };

    private static bool ShouldWarnLazy(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (named.TypeKind == RoslynTypeKind.Interface)
        {
            return true;
        }

        string metadata = BuiltInLeaves.FullMetadataName(named.OriginalDefinition);
        switch (metadata)
        {
            case "System.Collections.Generic.Queue`1":
            case "System.Collections.Generic.Stack`1":
            case "System.Collections.Generic.LinkedList`1":
            case "System.Collections.ObjectModel.ReadOnlyCollection`1":
            case "System.Collections.Immutable.ImmutableList`1":
            case "System.Collections.Immutable.ImmutableQueue`1":
            case "System.Collections.Immutable.ImmutableStack`1":
                return false;
            default:
                return true;
        }
    }

    // ----- upward crawl -----------------------------------------------------------------------------------------------

    private void Crawl(ClosureType type, string path)
    {
        INamedTypeSymbol named = (INamedTypeSymbol)type.Symbol;
        for (INamedTypeSymbol? current = named.BaseType; current is not null; current = current.BaseType)
        {
            if (IsStructuralBase(current))
            {
                continue;
            }

            Child(current, path + " : " + current.Name);
        }

        foreach (INamedTypeSymbol iface in named.AllInterfaces)
        {
            if (IsExcludedInterface(iface))
            {
                continue;
            }

            Child(iface, path + " : " + iface.Name);
        }
    }

    private static bool IsStructuralBase(INamedTypeSymbol type)
        => type.SpecialType is SpecialType.System_ValueType or SpecialType.System_Enum or SpecialType.System_Array or SpecialType.System_Delegate or SpecialType.System_MulticastDelegate;

    private bool IsExcludedInterface(INamedTypeSymbol iface)
    {
        // An interface with an unimplemented static abstract member can never be an IEqualityComparer<T> argument (CS8920).
        if (TypeArgumentRules.IsConstraintOnlyInterface(iface))
        {
            return true;
        }

        string ns = iface.ContainingNamespace?.ToDisplayString() ?? string.Empty;
        if (ns == "System" || ns.StartsWith("System.", StringComparison.Ordinal))
        {
            return true;
        }

        foreach (string prefix in _options.ExcludeInterfacesByPrefix)
        {
            if (ns == prefix || ns.StartsWith(prefix + ".", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // ----- built-in admission and dispatch cases -----------------------------------------------------------------------

    private void AdmitBuiltInLeaves()
    {
        foreach (BuiltInLeaves.Entry entry in BuiltInLeaves.All)
        {
            INamedTypeSymbol? symbol = CapabilityProbe.Find(_compilation, entry.MetadataName);
            if (symbol is null || _types.ContainsKey(symbol) || FindCustom(symbol) is not null)
            {
                continue;
            }

            ClosureType admitted = WithEquatable(new ClosureType(symbol, TypeKind.Leaf)
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
        foreach (ClosureType dispatch in _ordered.Where(t => t.IsDispatchCapable).ToList())
        {
            List<ClosureType> assignable = new List<ClosureType>();
            List<ClosureType> exact = new List<ClosureType>();
            foreach (ClosureType candidate in _ordered)
            {
                if (ReferenceEquals(candidate, dispatch) || candidate.Kind == TypeKind.Nullable)
                {
                    continue;
                }

                if (!_compilation.IsAssignable(candidate.Symbol, dispatch.Symbol))
                {
                    continue;
                }

                bool isDispatchOnly = candidate.Kind == TypeKind.Dispatch;
                if (isDispatchOnly)
                {
                    continue;   // an abstract case can never be the exact runtime type
                }

                if (candidate.Symbol is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T })
                {
                    continue;   // boxing erases nullable origin: a box holds S or is null, never Nullable<S>
                }

                if (candidate.Kind == TypeKind.Leaf)
                {
                    if (candidate.Symbol.IsValueType)
                    {
                        exact.Add(candidate);       // boxed value leaves: exact type test, then unbox
                    }
                    else
                    {
                        assignable.Add(candidate);  // reference leaves, user simple and custom rules accept derived runtime types
                    }
                }
                else if (candidate.IsContainer)
                {
                    if (candidate.IsCanonicalCase)
                    {
                        assignable.Add(candidate);   // int[] and List<int> both select IEnumerable<int>; HashSet<T> and SortedSet<T> both select ISet<T>
                    }
                    else if (candidate.IsValueShape && !ImplementsAnyCanonical(candidate))
                    {
                        exact.Add(candidate);        // a value container behind a dispatch type without a canonical case of its own
                    }
                }
                else if (candidate.Kind == TypeKind.Tuple)
                {
                    exact.Add(candidate);
                }
                else
                {
                    exact.Add(candidate);
                }
            }

            if (dispatch.Kind == TypeKind.Class || dispatch.Symbol.SpecialType == SpecialType.System_Object)
            {
                exact.Add(dispatch);   // the exact-self case of an unsealed concrete class, or of plain object (zero members)
            }

            // Assignable cases: topological by assignability so a narrower rule precedes a broader one, then shape precedence,
            // then rule category, then name. Exact cases: by name; every runtime type matches at most one.
            assignable.Sort((a, b) => CompareAssignable(a, b));
            assignable = TopologicalByAssignability(assignable);
            exact.Sort((a, b) => string.CompareOrdinal(a.Symbol.ToDisplayString(), b.Symbol.ToDisplayString()));

            foreach (ClosureType a in assignable)
            {
                dispatch.Cases.Add((a, false));
            }

            foreach (ClosureType e in exact)
            {
                dispatch.Cases.Add((e, true));
            }

            if (dispatch.Kind == TypeKind.Dispatch && dispatch.Cases.Count == 0 && dispatch.Symbol.SpecialType != SpecialType.System_Object)
            {
                _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.NoDispatchCases, LocationInfo.From(dispatch.Symbol), dispatch.Symbol.ToDisplayString()));
            }
        }
    }

    private bool ImplementsAnyCanonical(ClosureType candidate)
        => _ordered.Any(t => t.IsCanonicalCase && _compilation.IsAssignable(candidate.Symbol, t.Symbol));

    private static int CompareAssignable(ClosureType a, ClosureType b)
    {
        int shape = ShapeRank(a).CompareTo(ShapeRank(b));
        if (shape != 0)
        {
            return shape;
        }

        int category = CategoryRank(a).CompareTo(CategoryRank(b));
        if (category != 0)
        {
            return category;
        }

        return string.CompareOrdinal(a.Symbol.ToDisplayString(), b.Symbol.ToDisplayString());
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
        List<ClosureType> result = new List<ClosureType>();
        foreach (ClosureType candidate in cases)
        {
            int insertAt = result.Count;
            for (int i = 0; i < result.Count; i++)
            {
                if (_compilation.IsAssignable(candidate.Symbol, result[i].Symbol) && !SymbolEqualityComparer.Default.Equals(candidate.Symbol, result[i].Symbol))
                {
                    insertAt = i;
                    break;
                }
            }

            result.Insert(insertAt, candidate);
        }

        return result;
    }
}
