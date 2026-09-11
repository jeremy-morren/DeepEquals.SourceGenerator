// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

namespace DeepEquals.SourceGenerator.Model;

/// <summary>The semantic kind of a closure type; decides which cores exist and which rule compares it.</summary>
internal enum TypeKind
{
    /// <summary>Compared by one call and never walked: built-in rule, enum, user simple type or custom comparer.</summary>
    Leaf,

    /// <summary>Wrapper-only node; lowered to HasValue plus the payload's rule at every use site.</summary>
    Nullable,

    /// <summary>A concrete class or record compared by its selected members. Unsealed ones also dispatch.</summary>
    Class,

    /// <summary>A struct compared by its selected members.</summary>
    Struct,

    /// <summary>object, an interface or an abstract class: a dispatch core and nothing else.</summary>
    Dispatch,

    /// <summary>T[] with a span core.</summary>
    Array,

    /// <summary>List&lt;T&gt; through CollectionsMarshal where available, otherwise the indexer.</summary>
    List,

    /// <summary>ImmutableArray&lt;T&gt; with default preflight.</summary>
    ImmutableArray,

    /// <summary>ArraySegment&lt;T&gt;: the represented slice.</summary>
    ArraySegment,

    /// <summary>Memory&lt;T&gt; or ReadOnlyMemory&lt;T&gt;: the represented span.</summary>
    Memory,

    /// <summary>IReadOnlyList&lt;T&gt; or IList&lt;T&gt;.</summary>
    ListInterface,

    /// <summary>IReadOnlyCollection&lt;T&gt; or IEnumerable&lt;T&gt;, an ordered sequence.</summary>
    EnumerableInterface,

    /// <summary>ISet&lt;T&gt;, IReadOnlySet&lt;T&gt; or an implementation: unordered multiset.</summary>
    Set,

    /// <summary>IDictionary or IReadOnlyDictionary or an implementation: unordered multiset of pairs.</summary>
    Dictionary,

    /// <summary>KeyValuePair&lt;K,V&gt;.</summary>
    KeyValuePair,

    /// <summary>System.ValueTuple of any arity, Rest flattened.</summary>
    ValueTuple,

    /// <summary>System.Tuple of any arity, Rest flattened; a reference product.</summary>
    Tuple,
}

/// <summary>Which explicit rule a leaf uses.</summary>
internal enum LeafRule
{
    /// <summary>EqualityComparer&lt;T&gt;.Default and GetHashCode(); the type is default-equality-compatible.</summary>
    Default,

    /// <summary>Operator == and the value itself (or GetHashCode()) as the hash: primitives, char, bool.</summary>
    Primitive,

    /// <summary>A 64-bit or wider integer: == and DeepEqualsHashCode.Hash.</summary>
    WideInteger,

    /// <summary>string.Equals ordinal and DeepEqualsHashCode.Hash(string).</summary>
    String,

    /// <summary>Bitwise float.</summary>
    Single,

    /// <summary>Bitwise double.</summary>
    Double,

    /// <summary>Bitwise Half.</summary>
    Half,

    /// <summary>Exact decimal words.</summary>
    Decimal,

    /// <summary>Complete 64-bit DateTime storage.</summary>
    DateTime,

    /// <summary>Ticks and offset.</summary>
    DateTimeOffset,

    /// <summary>Ordinal OriginalString plus IsAbsoluteUri.</summary>
    Uri,

    /// <summary>Reference identity.</summary>
    Regex,

    /// <summary>Guid: == and DeepEqualsHashCode.Hash(Guid).</summary>
    Guid,

    /// <summary>An enum: cast to the underlying type.</summary>
    Enum,

    /// <summary>A floating-point aggregate: every listed component by the bitwise rule.</summary>
    FloatAggregate,

    /// <summary>A user [SimpleType]: EqualityComparer&lt;TStatic&gt;.Default.</summary>
    UserSimple,

    /// <summary>A [CustomEqualityComparer] registration.</summary>
    Custom,
}

/// <summary>How a selected member is read.</summary>
internal enum MemberAccess
{
    /// <summary>The context can name the field: read it directly.</summary>
    Direct,

    /// <summary>
    /// Compiler storage behind an auto-property whose getter the compiler wrote: read through that getter, which returns
    /// exactly the backing field and inlines to the same load. Needs no accessor, no reflection and nothing a trimmer can remove.
    /// </summary>
    Getter,

    /// <summary>An [UnsafeAccessor] extern on the context.</summary>
    UnsafeAccessor,

    /// <summary>A cached expression-tree delegate in a lazy holder.</summary>
    Delegate,
}

/// <summary>The cost class that orders member comparisons cheapest first.</summary>
internal enum MemberCost
{
    Primitive,
    SimpleLeaf,
    String,
    Struct,
    Nullable,
    Reference,
    Product,
    Collection,
}

/// <summary>One selected instance field of a deep type, base members first.</summary>
internal sealed record MemberModel(
    string Name,
    string FieldMetadataName,
    string AccessorName,
    string DeclaringTypeGlobalName,
    string DeclaringTypeShortName,
    bool DeclaringTypeIsValueType,
    bool DeclaringTypeIsGeneric,
    int TypeId,
    MemberAccess Access,
    MemberCost Cost,
    bool IsSameScc,
    int DeclarationOrder,
    bool GenericAccessor,
    string GenericHolderName,
    string GenericTypeParameters,
    string GenericTypeArguments,
    string GenericConstraints,
    string OpenDeclaringTypeGlobalName,
    string OpenFieldTypeGlobalName);

/// <summary>
/// One case of a dispatch core. For an exact case, <paramref name="ResolvedAssignable"/> is the index among the
/// assignable cases of the one its runtime type converts to first, or -1 when it converts to none. An assignable case is
/// <paramref name="Hoistable"/> when its type is sealed and converts to no earlier case: its test may then run first.
/// </summary>
internal sealed record DispatchCase(int TypeId, bool IsExact, bool IsSameScc, int ResolvedAssignable, bool Hoistable);

/// <summary>
/// One runtime size check behind a bit-block struct: the struct, or a struct nested in it, must occupy exactly
/// <paramref name="Size"/> bytes, the sum of its fields, for its bytes to be its value.
/// </summary>
internal sealed record BitBlockCheck(string GlobalName, int Size);

/// <summary>The component of a floating-point aggregate leaf.</summary>
internal sealed record AggregateComponent(string Expression, bool IsDouble);

/// <summary>A registered custom comparer, resolved.</summary>
internal sealed record CustomComparerModel(
    int Index,
    string ComparerTypeGlobalName,
    string TargetTypeGlobalName,
    string HolderName,
    string AcquisitionExpression,
    bool HolderTypedAsComparer,
    bool HandleNulls);

/// <summary>Everything the emitter needs about one closure type. Names are frozen here; emit never computes one.</summary>
internal sealed record TypeModel(
    int Id,
    TypeKind Kind,
    string GlobalName,
    string ShortName,
    bool IsValueType,
    bool IsReferenceTypeNullable,
    bool IsSealed,
    bool IsAbstract,
    bool IsInterface,
    bool IsObject,
    string Accessibility,
    bool EmitWrapper,
    bool EmitConvenienceProperty,
    bool IsUnsafe,
    bool IsRoot,
    // Leaf details
    LeafRule LeafRule,
    bool DefaultCompatible,
    bool ImplementsIEquatable,
    bool HasPublicEquatableEquals,
    string EnumUnderlyingGlobalName,
    int EnumUnderlyingWidth,
    bool EnumUnderlyingUnsigned,
    EquatableArray<AggregateComponent> AggregateComponents,
    int CustomComparerIndex,
    bool CustomWrapsNullable,
    // Composite details
    int PayloadTypeId,
    int ElementTypeId,
    int KeyTypeId,
    int ValueTypeId,
    EquatableArray<int> ItemTypeIds,
    bool ElementIsSameScc,
    bool KeyIsSameScc,
    bool ValueIsSameScc,
    EquatableArray<bool> ItemsAreSameScc,
    bool IsReadOnlyMemory,
    string CollectionInterfaceGlobalName,
    // Deep details
    EquatableArray<MemberModel> Members,
    EquatableArray<DispatchCase> Cases,
    int TailMemberIndex,
    bool PassByValue,
    bool InlineAsSmallStruct,
    bool HasStorageIgnoredByShape,
    // Graph results
    bool IsCyclic,
    bool IsGuarded,
    bool NeedsState,
    bool HasBoxedAdapter,
    bool BoxedAdapterGuarded,
    int GuardKind,
    int BoxedGuardKind,
    bool HasShallowHash,
    // The highest hash level emitted: 1 for the public hash alone, k when the matching fingerprint of an unordered
    // collection reaches this type and follows payload edges k deep. Levels 2..k are MatchHashCode_T_L{n}.
    int MatchHashLevels,
    // Bit blocks: a value whose equality is its storage bytes, with no references and no padding. BitBlockSize is its
    // size in bytes, or 0 when it is not one; a struct also carries the runtime size checks that must hold.
    int BitBlockSize,
    EquatableArray<BitBlockCheck> BitBlockChecks)
{
    public bool IsBitBlock => BitBlockSize > 0;
}
