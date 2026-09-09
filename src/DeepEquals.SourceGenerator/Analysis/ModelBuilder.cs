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

namespace DeepEquals.SourceGenerator.Analysis;

/// <summary>Freezes the symbol-level closure into the equatable model: names, the core call graph, guards, state, hash levels.</summary>
internal sealed class ModelBuilder
{
    private enum NodeKind
    {
        Dispatch,
        Semantic,
        Boxed,
    }

    private readonly Compilation _compilation;
    private readonly INamedTypeSymbol _context;
    private readonly ContextOptions _options;
    private readonly TargetCapabilities _capabilities;
    private readonly ClosureResult _closure;
    private readonly List<DiagnosticInfo> _diagnostics;
    private readonly CancellationToken _cancellationToken;
    private readonly List<ClosureType> _types;
    private readonly Dictionary<ClosureType, string> _shortNames = new Dictionary<ClosureType, string>();
    private readonly Dictionary<(ClosureType, NodeKind), int> _nodes = new Dictionary<(ClosureType, NodeKind), int>();
    private readonly List<(ClosureType Type, NodeKind Kind)> _nodeList = new List<(ClosureType, NodeKind)>();
    private readonly List<List<int>> _edges = new List<List<int>>();
    private int[] _scc = Array.Empty<int>();
    private bool[] _cyclic = Array.Empty<bool>();
    private bool[] _reachesCyclic = Array.Empty<bool>();
    private bool[] _reachesUnsafe = Array.Empty<bool>();
    private readonly Dictionary<ClosureType, int> _guardKinds = new Dictionary<ClosureType, int>();
    private readonly Dictionary<ClosureType, int> _boxedGuardKinds = new Dictionary<ClosureType, int>();
    private readonly Dictionary<ClosureType, int> _sizeEstimates = new Dictionary<ClosureType, int>();

    public ModelBuilder(Compilation compilation, INamedTypeSymbol context, ContextOptions options, TargetCapabilities capabilities, ClosureResult closure, List<DiagnosticInfo> diagnostics, CancellationToken cancellationToken)
    {
        _compilation = compilation;
        _context = context;
        _options = options;
        _capabilities = capabilities;
        _closure = closure;
        _diagnostics = diagnostics;
        _cancellationToken = cancellationToken;
        _types = closure.Types.OrderBy(t => t.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), StringComparer.Ordinal).ToList();
        for (int i = 0; i < _types.Count; i++)
        {
            _types[i].Id = i;
        }
    }

    public ContextModel Build(string hintName, LocationInfo? location)
    {
        AssignNames();
        _cancellationToken.ThrowIfCancellationRequested();
        BuildGraph();
        Tarjan();
        Propagate();
        AssignKinds();
        _cancellationToken.ThrowIfCancellationRequested();
        CheckUserMembers();

        List<CustomComparerModel> customs = BuildCustomComparers();
        List<AccessorHolderModel> holders = new List<AccessorHolderModel>();
        List<TypeModel> models = new List<TypeModel>(_types.Count);
        bool anyUnsafe = false;
        foreach (ClosureType type in _types)
        {
            TypeModel model = BuildType(type, customs);
            anyUnsafe |= model.IsUnsafe;
            models.Add(model);
        }

        foreach (IGrouping<INamedTypeSymbol, (ClosureType Owner, ClosureMember Member)> group in _types
            .Where(t => t.IsDeep)
            .SelectMany(t => t.Members.Where(m => m.Access == MemberAccess.Delegate).Select(m => (Owner: t, Member: m)))
            .GroupBy(x => x.Member.Declaring, (IEqualityComparer<INamedTypeSymbol>)SymbolEqualityComparer.Default)
            .OrderBy(g => g.Key.ToDisplayString(), StringComparer.Ordinal))
        {
            List<MemberModel> members = group
                .Select(x => BuildMember(x.Owner, x.Member))
                .GroupBy(m => m.FieldMetadataName, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderBy(m => m.FieldMetadataName, StringComparer.Ordinal)
                .ToList();
            holders.Add(new AccessorHolderModel(
                DeclaringShort(group.Key) + "_Accessors",
                group.Key.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                group.Key.IsValueType,
                EquatableArray.Create(members)));
        }

        string ns = _context.ContainingNamespace is null || _context.ContainingNamespace.IsGlobalNamespace ? string.Empty : _context.ContainingNamespace.ToDisplayString();
        List<string> containing = new List<string>();
        for (INamedTypeSymbol? outer = _context.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            containing.Add(outer.Name);
        }

        containing.Reverse();

        return new ContextModel(
            hintName,
            ns,
            EquatableArray.Create(containing),
            _context.Name,
            AccessibilityKeyword(EffectiveAccessibility(_context)),
            location,
            _options,
            _capabilities,
            EquatableArray.Create(models),
            EquatableArray.Create(customs),
            EquatableArray.Create(holders),
            EquatableArray.Create(CollectSuppressedIds()),
            anyUnsafe,
            EquatableArray.Create(_diagnostics));
    }

    // ----- naming -----------------------------------------------------------------------------------------------------

    private void AssignNames()
    {
        Dictionary<ClosureType, int> rung = _types.ToDictionary(t => t, _ => 0);
        for (int iteration = 0; iteration < 8; iteration++)
        {
            _shortNames.Clear();
            foreach (ClosureType type in _types)
            {
                _shortNames[type] = NameAt(type, rung[type]);
            }

            // Every identifier a type claims is checked against every other type's claims and against the context's own name.
            Dictionary<string, ClosureType> claims = new Dictionary<string, ClosureType>(StringComparer.Ordinal);
            HashSet<ClosureType> colliding = new HashSet<ClosureType>();
            foreach (ClosureType type in _types)
            {
                foreach (string identifier in Naming.IdentifiersFor(_shortNames[type]))
                {
                    if (identifier == _context.Name || Array.IndexOf(Naming.ReservedNames, identifier) >= 0 && identifier == _shortNames[type] && !Array.Exists(Naming.HazardousNames, h => h == identifier))
                    {
                        colliding.Add(type);
                        continue;
                    }

                    if (claims.TryGetValue(identifier, out ClosureType? owner))
                    {
                        if (!ReferenceEquals(owner, type))
                        {
                            colliding.Add(type);
                            colliding.Add(owner);
                        }
                    }
                    else
                    {
                        claims[identifier] = type;
                    }
                }
            }

            if (colliding.Count == 0)
            {
                return;
            }

            bool advanced = false;
            foreach (ClosureType type in colliding)
            {
                if (rung[type] < 6)
                {
                    rung[type]++;
                    advanced = true;
                }
            }

            if (!advanced)
            {
                List<ClosureType> pair = colliding.OrderBy(t => t.Id).Take(2).ToList();
                _diagnostics.Add(DiagnosticInfo.Create(
                    Diagnostics.IdentifierCollision,
                    null,
                    pair[0].Symbol.ToDisplayString(),
                    pair.Count > 1 ? pair[1].Symbol.ToDisplayString() : _context.Name,
                    _shortNames[pair[0]]));
                return;
            }
        }
    }

    private static string NameAt(ClosureType type, int rung)
    {
        switch (rung)
        {
            case 0:
            case 1:
            case 2:
            case 3:
                return Naming.Candidate(type.Symbol, rung);
            case 4:
                return Naming.Candidate(type.Symbol, 3) + "__" + Naming.Digest(type.Symbol, 16);
            default:
                return Naming.Candidate(type.Symbol, 3) + "__" + Naming.Digest(type.Symbol, 64);
        }
    }

    private string Short(ClosureType type) => _shortNames[type];

    private string DeclaringShort(INamedTypeSymbol declaring)
    {
        ClosureType? type = _types.FirstOrDefault(t => SymbolEqualityComparer.Default.Equals(t.Symbol, declaring));
        return type is not null ? Short(type) : Naming.Candidate(declaring, 2);
    }

    private void CheckUserMembers()
    {
        HashSet<string> generated = new HashSet<string>(StringComparer.Ordinal);
        foreach (ClosureType type in _types)
        {
            foreach (string identifier in Naming.IdentifiersFor(Short(type)))
            {
                generated.Add(identifier);
            }
        }

        foreach (string reserved in Naming.ReservedNames)
        {
            generated.Add(reserved);
        }

        foreach (ISymbol member in _context.GetMembers())
        {
            if (member.IsImplicitlyDeclared || member is IMethodSymbol { MethodKind: MethodKind.Constructor or MethodKind.StaticConstructor })
            {
                continue;
            }

            if (generated.Contains(member.Name))
            {
                _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.UserMemberCollision, LocationInfo.From(member), member.Name, member.Name));
            }
        }
    }

    // ----- graph ------------------------------------------------------------------------------------------------------

    private int Node(ClosureType type, NodeKind kind)
    {
        if (!_nodes.TryGetValue((type, kind), out int id))
        {
            id = _nodeList.Count;
            _nodes[(type, kind)] = id;
            _nodeList.Add((type, kind));
            _edges.Add(new List<int>());
        }

        return id;
    }

    private bool HasNode(ClosureType type, NodeKind kind) => _nodes.ContainsKey((type, kind));

    private int? Entry(ClosureType type)
    {
        switch (type.Kind)
        {
            case TypeKind.Leaf:
                return null;
            case TypeKind.Nullable:
                return type.Payload is null ? null : Entry(type.Payload);
            case TypeKind.Dispatch:
                return Node(type, NodeKind.Dispatch);
            case TypeKind.Class:
                return type.Symbol.IsSealed ? Node(type, NodeKind.Semantic) : Node(type, NodeKind.Dispatch);
            default:
                return Node(type, NodeKind.Semantic);
        }
    }

    private void BuildGraph()
    {
        // Create every node first so ids are stable, then edges.
        foreach (ClosureType type in _types)
        {
            if (type.IsDispatchCapable)
            {
                Node(type, NodeKind.Dispatch);
            }

            if (type.Kind is not (TypeKind.Leaf or TypeKind.Nullable or TypeKind.Dispatch))
            {
                Node(type, NodeKind.Semantic);
            }
        }

        foreach (ClosureType dispatch in _types.Where(t => t.IsDispatchCapable))
        {
            foreach ((ClosureType c, bool exact) in dispatch.Cases)
            {
                if (exact && c.IsValueShape)
                {
                    Node(c, NodeKind.Boxed);
                }
            }
        }

        foreach (ClosureType type in _types)
        {
            if (type.IsDeep)
            {
                int body = Node(type, NodeKind.Semantic);
                foreach (ClosureMember member in type.Members)
                {
                    Link(body, Entry(member.Type));
                }
            }
            else if (type.IsContainer)
            {
                int node = Node(type, NodeKind.Semantic);
                if (type.Kind == TypeKind.Dictionary)
                {
                    Link(node, Entry(type.Key!));
                    Link(node, Entry(type.Value!));
                }
                else
                {
                    Link(node, Entry(type.Element!));
                }
            }
            else if (type.IsProduct)
            {
                int node = Node(type, NodeKind.Semantic);
                if (type.Kind == TypeKind.KeyValuePair)
                {
                    Link(node, Entry(type.Key!));
                    Link(node, Entry(type.Value!));
                }
                else
                {
                    foreach (ClosureType item in type.Items)
                    {
                        Link(node, Entry(item));
                    }
                }
            }

            if (type.IsDispatchCapable)
            {
                int dispatch = Node(type, NodeKind.Dispatch);
                foreach ((ClosureType c, bool exact) in type.Cases)
                {
                    Link(dispatch, CaseTarget(c, exact));
                }
            }

            if (HasNode(type, NodeKind.Boxed))
            {
                Link(Node(type, NodeKind.Boxed), Node(type, NodeKind.Semantic));
            }
        }
    }

    private int? CaseTarget(ClosureType c, bool exact)
    {
        if (c.Kind == TypeKind.Leaf || (exact && c.Symbol.SpecialType == SpecialType.System_Object))
        {
            return null;   // leaves and the zero-member object self case have no core to reach
        }

        if (exact && c.IsValueShape)
        {
            return Node(c, NodeKind.Boxed);
        }

        return c.Kind is TypeKind.Class or TypeKind.Struct || c.IsContainer || c.IsProduct ? Node(c, NodeKind.Semantic) : Entry(c);
    }

    private void Link(int from, int? to)
    {
        if (to is int target)
        {
            _edges[from].Add(target);
        }
    }

    private void Tarjan()
    {
        int n = _nodeList.Count;
        _scc = new int[n];
        _cyclic = new bool[n];
        int[] index = new int[n];
        int[] low = new int[n];
        bool[] onStack = new bool[n];
        for (int i = 0; i < n; i++)
        {
            index[i] = -1;
        }

        Stack<int> stack = new Stack<int>();
        int counter = 0;
        int components = 0;

        // Iterative Tarjan so a large closure cannot overflow the generator's stack.
        for (int root = 0; root < n; root++)
        {
            if (index[root] != -1)
            {
                continue;
            }

            Stack<(int Node, int Next)> frames = new Stack<(int, int)>();
            frames.Push((root, 0));
            index[root] = low[root] = counter++;
            stack.Push(root);
            onStack[root] = true;

            while (frames.Count > 0)
            {
                (int v, int next) = frames.Pop();
                if (next < _edges[v].Count)
                {
                    frames.Push((v, next + 1));
                    int w = _edges[v][next];
                    if (index[w] == -1)
                    {
                        index[w] = low[w] = counter++;
                        stack.Push(w);
                        onStack[w] = true;
                        frames.Push((w, 0));
                    }
                    else if (onStack[w])
                    {
                        low[v] = Math.Min(low[v], index[w]);
                    }

                    continue;
                }

                if (frames.Count > 0)
                {
                    int parent = frames.Peek().Node;
                    low[parent] = Math.Min(low[parent], low[v]);
                }

                if (low[v] == index[v])
                {
                    int size = 0;
                    int w;
                    do
                    {
                        w = stack.Pop();
                        onStack[w] = false;
                        _scc[w] = components;
                        size++;
                    }
                    while (w != v);

                    components++;
                    if (size > 1)
                    {
                        // mark below once all members are known
                    }
                }
            }
        }

        int[] sizes = new int[components];
        foreach (int c in _scc)
        {
            sizes[c]++;
        }

        for (int v = 0; v < n; v++)
        {
            _cyclic[v] = sizes[_scc[v]] > 1 || _edges[v].Contains(v);
        }
    }

    private void Propagate()
    {
        int n = _nodeList.Count;
        _reachesCyclic = (bool[])_cyclic.Clone();
        _reachesUnsafe = new bool[n];
        for (int v = 0; v < n; v++)
        {
            (ClosureType type, NodeKind kind) = _nodeList[v];
            if (kind == NodeKind.Semantic && type.IsDeep && type.Members.Any(m => m.Access == MemberAccess.Delegate))
            {
                _reachesUnsafe[v] = true;
            }
        }

        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int v = 0; v < n; v++)
            {
                foreach (int w in _edges[v])
                {
                    if (_reachesCyclic[w] && !_reachesCyclic[v])
                    {
                        _reachesCyclic[v] = true;
                        changed = true;
                    }

                    if (_reachesUnsafe[w] && !_reachesUnsafe[v])
                    {
                        _reachesUnsafe[v] = true;
                        changed = true;
                    }
                }
            }
        }
    }

    private bool IsGuardedNode(int node)
    {
        if (!_cyclic[node])
        {
            return false;
        }

        (ClosureType type, NodeKind kind) = _nodeList[node];
        switch (kind)
        {
            case NodeKind.Boxed:
                return true;
            case NodeKind.Semantic:
                if (type.Kind == TypeKind.Class || type.Kind == TypeKind.Tuple)
                {
                    return true;
                }

                return type.IsContainer && !type.Symbol.IsValueType;
            default:
                return false;
        }
    }

    private void AssignKinds()
    {
        int next = 1;
        foreach (ClosureType type in _types)
        {
            if (HasNode(type, NodeKind.Semantic) && IsGuardedNode(Node(type, NodeKind.Semantic)))
            {
                _guardKinds[type] = next++;
            }

            if (HasNode(type, NodeKind.Boxed) && IsGuardedNode(Node(type, NodeKind.Boxed)))
            {
                _boxedGuardKinds[type] = next++;
            }
        }
    }

    private bool SameScc(int? a, int? b) => a is int x && b is int y && _scc[x] == _scc[y];

    private bool NeedsState(ClosureType type)
    {
        int? entry = Entry(type);
        return entry is int e && _reachesCyclic[e];
    }

    private bool IsUnsafe(ClosureType type)
    {
        int? entry = Entry(type);
        return entry is int e && _reachesUnsafe[e];
    }

    // ----- type models ------------------------------------------------------------------------------------------------

    private TypeModel BuildType(ClosureType type, List<CustomComparerModel> customs)
    {
        ITypeSymbol symbol = type.Symbol;
        string shortName = Short(type);
        bool hazardous = Array.IndexOf(Naming.HazardousNames, shortName) >= 0;
        if (hazardous && type.Reached)
        {
            _diagnostics.Add(DiagnosticInfo.Create(Diagnostics.HazardousConvenienceName, null, symbol.ToDisplayString(), shortName));
        }

        int? semantic = HasNode(type, NodeKind.Semantic) ? Node(type, NodeKind.Semantic) : (int?)null;
        int? dispatch = HasNode(type, NodeKind.Dispatch) ? Node(type, NodeKind.Dispatch) : (int?)null;
        int? boxed = HasNode(type, NodeKind.Boxed) ? Node(type, NodeKind.Boxed) : (int?)null;
        bool cyclic = (semantic is int s && _cyclic[s]) || (dispatch is int d && _cyclic[d]);

        List<MemberModel> members = new List<MemberModel>();
        int tailIndex = -1;
        if (type.IsDeep)
        {
            List<ClosureMember> ordered = type.Members.ToList();
            if (type.Kind == TypeKind.Class && symbol.IsSealed)
            {
                int lastSelf = -1;
                for (int i = 0; i < ordered.Count; i++)
                {
                    if (ReferenceEquals(ordered[i].Type, type))
                    {
                        lastSelf = i;
                    }
                }

                if (lastSelf >= 0)
                {
                    ClosureMember tail = ordered[lastSelf];
                    ordered.RemoveAt(lastSelf);
                    ordered.Add(tail);
                    tailIndex = ordered.Count - 1;
                }
            }

            foreach (ClosureMember member in ordered)
            {
                members.Add(BuildMember(type, member));
            }
        }

        List<DispatchCase> cases = new List<DispatchCase>();
        if (type.IsDispatchCapable)
        {
            foreach ((ClosureType c, bool exact) in type.Cases)
            {
                cases.Add(new DispatchCase(c.Id, exact, SameScc(dispatch, CaseTarget(c, exact))));
            }
        }

        bool passByValue = true;
        bool inline = false;
        if (type.Kind == TypeKind.Struct)
        {
            int estimate = EstimateSize(type, _options.StructPassByValueMaxByteSize + 1);
            passByValue = estimate <= _options.StructPassByValueMaxByteSize;
            inline = type.Members.Count <= 4 && type.Members.All(m => m.Type.Kind == TypeKind.Leaf && m.Access == MemberAccess.Direct && m.Type.LeafRule != LeafRule.Custom);
        }

        List<bool> itemsSameScc = new List<bool>();
        foreach (ClosureType item in type.Items)
        {
            itemsSameScc.Add(SameScc(semantic, Entry(item)));
        }

        INamedTypeSymbol? named = symbol as INamedTypeSymbol;
        bool unsafeType = IsUnsafe(type);
        bool emitWrapper = type.Reached;
        CustomComparerModel? custom = type.Custom is not null ? customs.FirstOrDefault(c => c.Index == type.Custom.Index) : null;

        return new TypeModel(
            Id: type.Id,
            Kind: type.Kind,
            GlobalName: symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            ShortName: shortName,
            IsValueType: symbol.IsValueType,
            IsReferenceTypeNullable: !symbol.IsValueType,
            IsSealed: symbol.IsSealed || symbol.IsValueType,
            IsAbstract: symbol.IsAbstract && symbol.TypeKind != RoslynTypeKind.Interface,
            IsInterface: symbol.TypeKind == RoslynTypeKind.Interface,
            IsObject: symbol.SpecialType == SpecialType.System_Object,
            Accessibility: AccessibilityKeyword(Min(EffectiveAccessibility(symbol), EffectiveAccessibility(_context))),
            EmitWrapper: emitWrapper,
            EmitConvenienceProperty: emitWrapper && !hazardous,
            IsUnsafe: unsafeType,
            IsRoot: type.IsRoot,
            LeafRule: type.LeafRule,
            DefaultCompatible: type.DefaultCompatible,
            ImplementsIEquatable: type.ImplementsIEquatable,
            HasPublicEquatableEquals: type.HasPublicEquatableEquals,
            EnumUnderlyingGlobalName: named?.EnumUnderlyingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty,
            EnumUnderlyingWidth: named?.EnumUnderlyingType is not null ? type.Width : 0,
            EnumUnderlyingUnsigned: named?.EnumUnderlyingType?.SpecialType is SpecialType.System_Byte or SpecialType.System_UInt16 or SpecialType.System_UInt32 or SpecialType.System_UInt64,
            AggregateComponents: new EquatableArray<AggregateComponent>(type.AggregateComponents),
            CustomComparerIndex: custom?.Index ?? -1,
            CustomWrapsNullable: type.CustomWrapsNullable,
            PayloadTypeId: type.Payload?.Id ?? -1,
            ElementTypeId: type.Element?.Id ?? -1,
            KeyTypeId: type.Key?.Id ?? -1,
            ValueTypeId: type.Value?.Id ?? -1,
            ItemTypeIds: new EquatableArray<int>(type.Items.Select(i => i.Id).ToArray()),
            ElementIsSameScc: type.Element is not null && SameScc(semantic, Entry(type.Element)),
            KeyIsSameScc: type.Key is not null && SameScc(semantic, Entry(type.Key)),
            ValueIsSameScc: type.Value is not null && SameScc(semantic, Entry(type.Value)),
            ItemsAreSameScc: EquatableArray.Create(itemsSameScc),
            IsReadOnlyMemory: type.IsReadOnlyMemory,
            CollectionInterfaceGlobalName: type.CollectionInterface?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? string.Empty,
            Members: EquatableArray.Create(members),
            Cases: EquatableArray.Create(cases),
            TailMemberIndex: tailIndex,
            PassByValue: passByValue,
            InlineAsSmallStruct: inline,
            HasStorageIgnoredByShape: type.HasStorageIgnoredByShape,
            IsCyclic: cyclic,
            IsGuarded: _guardKinds.ContainsKey(type),
            NeedsState: NeedsState(type),
            HasBoxedAdapter: boxed is not null,
            BoxedAdapterGuarded: _boxedGuardKinds.ContainsKey(type),
            GuardKind: _guardKinds.TryGetValue(type, out int kind) ? kind : 0,
            BoxedGuardKind: _boxedGuardKinds.TryGetValue(type, out int boxedKind) ? boxedKind : 0,
            HasShallowHash: cyclic);
    }

    private MemberModel BuildMember(ClosureType owner, ClosureMember member)
    {
        string declaringShort = DeclaringShort(member.Declaring);
        string segment = Naming.EscapeSegment(member.Name);
        int? body = HasNode(owner, NodeKind.Semantic) ? Node(owner, NodeKind.Semantic) : (int?)null;

        // A generic declaring type gets one accessor per (definition, field) whose type parameters mirror the definition's.
        // The runtime maps a generic static holder's class type parameters to the declaring type's, so one holder per
        // (definition) carries the accessors, and call sites supply the constructed arguments.
        bool genericAccessor = member.Access == MemberAccess.UnsafeAccessor && ClosureBuilder.IsGenericContext(member.Declaring);
        string accessorName = declaringShort + "_" + segment;
        string holderName = string.Empty;
        string typeParameters = string.Empty;
        string typeArguments = string.Empty;
        string constraints = string.Empty;
        string openDeclaring = string.Empty;
        string openField = string.Empty;
        if (genericAccessor)
        {
            INamedTypeSymbol definition = member.Declaring.OriginalDefinition;
            List<ITypeParameterSymbol> parameters = new List<ITypeParameterSymbol>();
            for (INamedTypeSymbol? current = definition; current is not null; current = current.ContainingType)
            {
                parameters.InsertRange(0, current.TypeParameters);
            }

            List<ITypeSymbol> arguments = new List<ITypeSymbol>();
            for (INamedTypeSymbol? current = member.Declaring; current is not null; current = current.ContainingType)
            {
                arguments.InsertRange(0, current.TypeArguments);
            }

            accessorName = segment;
            holderName = "Generic_" + Naming.Candidate(definition, 0) + "_Accessors";
            typeParameters = "<" + string.Join(", ", parameters.Select(p => p.Name)) + ">";
            typeArguments = "<" + string.Join(", ", arguments.Select(a => a.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) + ">";
            constraints = string.Concat(parameters.Select(ConstraintClause));
            openDeclaring = definition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            openField = member.Field.OriginalDefinition.Type.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        return new MemberModel(
            Name: member.Name,
            FieldMetadataName: member.Field.Name,
            AccessorName: accessorName,
            DeclaringTypeGlobalName: member.Declaring.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            DeclaringTypeShortName: declaringShort,
            DeclaringTypeIsValueType: member.Declaring.IsValueType,
            DeclaringTypeIsGeneric: member.Declaring.IsGenericType,
            TypeId: member.Type.Id,
            Access: member.Access,
            Cost: member.Cost,
            IsSameScc: SameScc(body, Entry(member.Type)),
            DeclarationOrder: member.Order,
            GenericAccessor: genericAccessor,
            GenericHolderName: holderName,
            GenericTypeParameters: typeParameters,
            GenericTypeArguments: typeArguments,
            GenericConstraints: constraints,
            OpenDeclaringTypeGlobalName: openDeclaring,
            OpenFieldTypeGlobalName: openField);
    }

    /// <summary>The where clause the runtime needs to match the declaring type's parameter; notnull has no runtime representation.</summary>
    private static string ConstraintClause(ITypeParameterSymbol parameter)
    {
        List<string> parts = new List<string>();
        if (parameter.HasUnmanagedTypeConstraint)
        {
            parts.Add("unmanaged");
        }
        else if (parameter.HasValueTypeConstraint)
        {
            parts.Add("struct");
        }
        else if (parameter.HasReferenceTypeConstraint)
        {
            parts.Add("class");
        }

        foreach (ITypeSymbol constraint in parameter.ConstraintTypes)
        {
            parts.Add(constraint.WithNullableAnnotation(NullableAnnotation.None).ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        if (parameter.HasConstructorConstraint)
        {
            parts.Add("new()");
        }

        return parts.Count == 0 ? string.Empty : $" where {parameter.Name} : {string.Join(", ", parts)}";
    }

    private List<CustomComparerModel> BuildCustomComparers()
    {
        List<CustomComparerModel> result = new List<CustomComparerModel>();
        int index = 0;
        foreach (CustomRegistration registration in _closure.Registrations.Custom.Where(c => !c.Ignored).OrderBy(c => c.Order))
        {
            ClosureType? target = _types.FirstOrDefault(t => SymbolEqualityComparer.Default.Equals(t.Symbol, registration.Target));
            if (target is null)
            {
                continue;
            }

            registration.Index = index;
            result.Add(new CustomComparerModel(
                index++,
                registration.ComparerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                registration.Target.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                Short(target) + "_ComparerHolder",
                registration.AcquisitionExpression,
                registration.HolderTypedAsComparer,
                registration.HandleNulls));
        }

        return result;
    }

    // ----- struct size --------------------------------------------------------------------------------------------------

    private int EstimateSize(ClosureType type, int saturate)
    {
        if (_sizeEstimates.TryGetValue(type, out int cached))
        {
            return cached;
        }

        _sizeEstimates[type] = saturate;   // provisional, guards recursion
        int total = EstimateSize(type.Symbol, saturate, 0);
        _sizeEstimates[type] = total;
        return total;
    }

    private int EstimateSize(ITypeSymbol symbol, int saturate, int depth)
    {
        if (depth > 16)
        {
            return saturate;
        }

        BuiltInLeaves.Entry? builtIn = BuiltInLeaves.Find(symbol);
        if (builtIn is not null)
        {
            return symbol.IsValueType ? Math.Max(1, builtIn.Width) : 8;
        }

        if (symbol.TypeKind == RoslynTypeKind.Enum)
        {
            return ((INamedTypeSymbol)symbol).EnumUnderlyingType is { } underlying ? EstimateSize(underlying, saturate, depth + 1) : 4;
        }

        if (!symbol.IsValueType || symbol is IPointerTypeSymbol || symbol is IFunctionPointerTypeSymbol)
        {
            return 8;
        }

        if (symbol is ITypeParameterSymbol || symbol.TypeKind == RoslynTypeKind.Error)
        {
            return saturate;
        }

        if (symbol is INamedTypeSymbol named)
        {
            int total = 0;
            foreach (IFieldSymbol field in named.GetMembers().OfType<IFieldSymbol>())
            {
                if (field.IsStatic || field.IsConst)
                {
                    continue;
                }

                total += EstimateSize(field.Type, saturate, depth + 1);
                if (total >= saturate)
                {
                    return saturate;
                }
            }

            return Math.Max(1, total);
        }

        return saturate;
    }

    // ----- accessibility ----------------------------------------------------------------------------------------------

    private static Accessibility EffectiveAccessibility(ITypeSymbol symbol)
    {
        switch (symbol)
        {
            case IArrayTypeSymbol array:
                return EffectiveAccessibility(array.ElementType);
            case INamedTypeSymbol named:
                Accessibility result = Accessibility.Public;
                for (INamedTypeSymbol? current = named; current is not null; current = current.ContainingType)
                {
                    result = Min(result, current.DeclaredAccessibility == Accessibility.NotApplicable ? Accessibility.Public : current.DeclaredAccessibility);
                }

                foreach (ITypeSymbol argument in named.TypeArguments)
                {
                    result = Min(result, EffectiveAccessibility(argument));
                }

                return result;
            default:
                return Accessibility.Public;
        }
    }

    private static Accessibility Min(Accessibility a, Accessibility b) => Rank(a) <= Rank(b) ? a : b;

    private static int Rank(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => 3,
        Accessibility.ProtectedOrInternal => 2,
        Accessibility.Internal => 2,
        _ => 1,
    };

    private static string AccessibilityKeyword(Accessibility accessibility) => Rank(accessibility) switch
    {
        3 => "public",
        2 => "internal",
        _ => "private",
    };

    // ----- suppressed diagnostics ---------------------------------------------------------------------------------------

    private List<string> CollectSuppressedIds()
    {
        SortedSet<string> ids = new SortedSet<string>(StringComparer.Ordinal) { "CS0612", "CS0618", "CS8632" };
        void Visit(ISymbol? symbol)
        {
            if (symbol is null)
            {
                return;
            }

            foreach (AttributeData attribute in symbol.GetAttributes())
            {
                string? name = attribute.AttributeClass?.ToDisplayString();
                switch (name)
                {
                    case KnownTypes.ObsoleteAttribute:
                        foreach (KeyValuePair<string, TypedConstant> argument in attribute.NamedArguments)
                        {
                            if (argument.Key == "DiagnosticId" && argument.Value.Value is string id && id.Length > 0)
                            {
                                ids.Add(id);
                            }
                        }

                        break;
                    case KnownTypes.ExperimentalAttribute:
                        if (attribute.ConstructorArguments.Length > 0 && attribute.ConstructorArguments[0].Value is string experimental && experimental.Length > 0)
                        {
                            ids.Add(experimental);
                        }

                        break;
                    case KnownTypes.RequiresPreviewFeaturesAttribute:
                        ids.Add("CA2252");
                        break;
                }
            }
        }

        foreach (ClosureType type in _types)
        {
            Visit(type.Symbol);
            if (type.Symbol is INamedTypeSymbol named)
            {
                foreach (ITypeSymbol argument in named.TypeArguments)
                {
                    Visit(argument);
                }
            }

            foreach (ClosureMember member in type.Members)
            {
                Visit(member.Field);
                Visit(member.Field.AssociatedSymbol);
            }
        }

        return ids.ToList();
    }
}
