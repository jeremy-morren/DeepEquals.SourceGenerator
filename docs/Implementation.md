# Implementation

What the generator emits for a context and what the runtime library `DeepEquals.SourceGeneration.Framework` does for it. Rules are stated once, without rationale. Diagnostics are referenced by id; see [Diagnostics](Diagnostics.md).

- [1. Vocabulary](#1-vocabulary)
- [2. The equality relation](#2-the-equality-relation)
- [3. The hash function](#3-the-hash-function)
- [4. Generated file](#4-generated-file)
- [5. Naming](#5-naming)
- [6. Cores](#6-cores)
- [7. Runtime state](#7-runtime-state)
- [8. Hash runtime](#8-hash-runtime)
- [9. Member selection and access](#9-member-selection-and-access)
- [10. Type graph analysis](#10-type-graph-analysis)
- [11. Target capabilities](#11-target-capabilities)
- [12. Pipeline](#12-pipeline)
- [13. Allocation contract](#13-allocation-contract)
- [14. Not implemented](#14-not-implemented)

## 1. Vocabulary

**Context.** A non-generic, non-abstract `partial` class deriving from `DeepEqualsContextBase` whose own declaration carries `[GenerateDeepEquals]` or `[DeepEqualsSourceGenerationOptions]` ([DEQ001](Diagnostics.md#context-declaration), [DEQ002](Diagnostics.md#context-declaration)). Attributes are collected along the base chain up to `DeepEqualsContextBase` and merged as a union; options merge per property, with the derived class winning and an unset property taking its default. Abstract bases emit nothing. The user-written half of the context is empty.

**Roots.** The types named by `[GenerateDeepEquals]`, with duplicates merged.

**Strategy.** Every static type gets exactly one strategy. The generator tests the rows below in order and the first match wins:

| Strategy | Matches when | Compared by |
|---|---|---|
| custom | the type is, or is assignable to, the `T` of a `[CustomEqualityComparer]`. Constructed types match instantiation-exactly. An exact `S` or `S?` registration beats a covering interface; `Base` beats `Derived` ([DEQ030](Diagnostics.md#custom-comparers-and-simple-types)); two unrelated covering interfaces are [DEQ031](Diagnostics.md#custom-comparers-and-simple-types) | the comparer instance; the type is a leaf |
| simple | the type is, or is assignable to, a `[SimpleType]` ([DEQ025](Diagnostics.md#custom-comparers-and-simple-types), [DEQ028](Diagnostics.md#custom-comparers-and-simple-types)) | the default rule in [§6.5](#65-leaves) for the exact static type in use |
| built-in leaf | one of the types in the [§6.5](#65-leaves) table | that table's rule |
| enum | any enum | its underlying integer |
| nullable | `Nullable<S>` | `HasValue`, then the strategy of `S` |
| product | `KeyValuePair<K,V>`, `ValueTuple<…>`, `Tuple<…>` | item by item |
| memory | `Memory<T>`, `ReadOnlyMemory<T>` | the represented span |
| dictionary | implements `IReadOnlyDictionary<K,V>` or `IDictionary<K,V>` | unordered matching of key-value pairs |
| set | implements `IReadOnlySet<T>` or `ISet<T>` | unordered matching of elements |
| ordered | `T[]`, `List<T>`, `ImmutableArray<T>`, `ArraySegment<T>`, then `IReadOnlyList<T>`/`IList<T>`, then `IReadOnlyCollection<T>`/`IEnumerable<T>` | element by element, in order |
| dispatch | `object`, an interface, an abstract class, or an unsealed class | the runtime type decides |
| deep | any other class, record or struct | its selected fields |

Only generic collection interfaces count. A type implementing several instantiations of the winning family is [DEQ015](Diagnostics.md#collections-and-shapes). A multi-dimensional array is [DEQ014](Diagnostics.md#collections-and-shapes) unless a leaf rule covers it. A collection-shaped non-framework type that also has its own storage is [DEQ027](Diagnostics.md#collections-and-shapes). A `System.*` type that falls through to *deep* is [DEQ024](Diagnostics.md#referenced-assemblies). Leaves are never shape-matched or walked, which is why `string` is not treated as an `IEnumerable<char>`.

**Closure.** The set of types the context generates code for. It starts with the roots and every type named by a strategy attribute, then grows until nothing new appears:

- the selected member types of every deep type;
- the element, key, value and item types of every shape;
- `S` for every `Nullable<S>`;
- for every deep concrete type, its base classes up to `object` (skipping `ValueType`, `Enum`, `Array`, `Delegate` and `MulticastDelegate`) and every interface it implements that is not excluded.

An interface is excluded when its namespace is `System` or starts with `System.`, when it matches `ExcludeInterfacesByPrefix`, or when it has an unimplemented `static abstract` member. Exclusion never applies to an explicit root or to a member's declared type. Construction stops with [DEQ018](Diagnostics.md#registrations) when a constructed type nests more than 8 generic levels deep or the closure passes 4,096 types. Derived types are never discovered.

**Selected members** of a deep type: every instance field of the type and its bases, including the compiler's `<Name>k__BackingField` for auto, positional-record and `field`-keyword properties and `<name>P` for captured primary-constructor parameters. Removed from that list: static and const fields, delegates and events ([DEQ005](Diagnostics.md#members)), anything targeted by `[DeepEqualsIgnore]` (on the field, its property, its parameter, or through the context-level `(Type, name)` form; [DEQ033](Diagnostics.md#members), [DEQ034](Diagnostics.md#members)), and members whose type contains `dynamic` ([DEQ023](Diagnostics.md#members)). A member whose type is inaccessible is [DEQ003](Diagnostics.md#members); a `ref struct`, pointer or `ref` field is [DEQ012](Diagnostics.md#members); a `fixed` buffer or `[InlineArray]` struct is [DEQ032](Diagnostics.md#members). Layout attributes are ignored and every selected field is compared once. Members are ordered cheapest first: primitives and enums, other simple types, strings, structs, nullables, references, products, collections.

## 2. The equality relation

This section defines when two values of a static type `T` are deep-equal. The definition is recursive, and for cyclic graphs it is **coinductive**: a pair of objects that is already being compared higher up the call stack is assumed equal rather than compared again. The generated code implements exactly this definition.

**Null and identity.** For reference and nullable types: the same reference on both sides is equal; null on exactly one side is unequal. The only exception is a custom rule with `handleNulls: true`, which receives nulls itself. Broad dispatch (`object`, interface, base class) always applies this rule itself, whatever the null policy of the case it ends up selecting.

**Leaves** (custom, simple, built-in, enum): the comparer or rule from [§6.5](#65-leaves). A custom comparer with `handleNulls: false` only ever sees non-null operands.

**Deep class.** Once the runtime types are known to be the same concrete type (sealed, or established by dispatch), two instances are equal when every selected member is deep-equal under the member's own static type. A class with no selected members compares equal.

**Struct.** The same member rule, with no null, identity or runtime-type step.

**Nullable** `S?`: both must agree on `HasValue`; when both have a value, the payloads are compared under `S`. An exact custom rule registered for `S?` is used instead and receives the nullable value as is.

**Product.** Each logical item is compared in order under its own type. For tuples the items are `Item1` onward, with `Rest` flattened when it is itself a tuple. Tuple element names play no part.

**Ordered collection.** The counts must match and each element must be deep-equal to the element at the same index. `ImmutableArray<T>`: two default values are equal; a default value differs from every initialized array, including an empty one. `ArraySegment<T>` and memory shapes compare only the represented slice, and a default value equals an empty one.

**Unordered collection.** The counts must match and there must be a one-to-one pairing between the two sides such that every pair is deep-equal. Entries are elements for sets and key-value pairs for dictionaries, with keys and values both compared deeply. The collection's own comparer is never consulted, which is why duplicates under deep equality can exist and the pairing must be one-to-one rather than "each left entry finds some match".

**Dispatch.** Each side independently picks the first *assignable case* it matches, in a fixed order (custom rules, simple rules, built-in leaves, canonical collection families; within those, a case that is assignable to another comes first, then shape precedence, then rule category, then metadata name). If both sides pick the same case, that case's rule decides. If only one side matches a case, the values are unequal. Otherwise the runtime types must be identical: different types are unequal; the same concrete deep type in the closure uses that type's rule; runtime type exactly `object` is equal; any other type throws `DeepEqualsUnknownTypeException`. Collection cases are canonical by family and closed type arguments, so an `int[]` and a `List<int>` behind `object` both take the ordered-`int` case, and a `HashSet<int>` and a `SortedSet<int>` both take the set-`int` case.

**Cycles.** Every guarded core has a kind constant. A top-level comparison keeps a set of `(kind, left, right)` triples that are in progress or already proved equal. Entering a guarded core with a triple already in the set returns `true`; otherwise the triple is added and the comparison proceeds. A `false` result is terminal, except inside an unordered trial, which is bracketed by `Mark` and `Rollback` so its triples are removed afterwards. Because of this, every triple in the set was either still in progress or proved equal, and "seen before" always means `true`. Consequences: a rolled cycle equals its unrolled form, and whether two members share one object or hold two equal copies is not observable.

Properties that follow: deep equality is reflexive, symmetric and transitive on well-typed graphs; unordered comparison does not depend on enumeration order, because of the exact matching in [§6.9](#69-unordered-algorithm); and equal values always produce equal hashes.

## 3. The hash function

Hashing is stateless and never looks at object identity. Null hashes to 0. Every library-defined collection hash returns 0 for null and a nonzero value for empty: if the count is 0 and the raw hash came out as 0, the result is 1. `Combine` and the streaming forms are xxHash32 with a per-process random seed ([§8](#8-hash-runtime)).

| Kind | Hash |
|---|---|
| leaf | the rule in [§6.5](#65-leaves); values wider than 32 bits go through the seeded `Hash(...)` overloads, never the BCL's xor fold |
| deep type | `Combine` over the member hashes in member order, nested when there are more than 32; no members gives the constant `1` |
| nullable | the payload hash when present, else 0 |
| product | `Combine` over the item hashes |
| ordered | the xxHash32 stream of the element hashes in order; the element count takes part through the stream length |
| unordered | `Combine(Count, sum of entry hashes)`, with the sum unchecked; a dictionary entry hashes as `Combine(keyHash, valueHash)` |
| dispatch | the hash of the selected case; runtime type exactly `object` gives `1` |

**Levels.** Every cyclic core that is not a dispatch core has two hash operations: level 1, `GetHashCode_T`, which callers use, and level 0, `ShallowHashCode_T`. Each edge in the call graph is classified once:

- A scalar or reference member, or a collection element, dictionary key or value, or tuple item, is a **payload** edge.
- Entering a product, memory shape or collection is a **structural** edge.
- Dispatch, boxing and nullable lowering are **transparent** edges.

Inside one strongly connected component, a payload edge taken from level 1 calls its target at level 0, and is left out entirely at level 0. Structural and transparent edges keep the level of the caller. An edge that leaves the component calls the target's ordinary full hash. A collection at level 0 hashes to its count (1 when empty); a product at level 0 with every item left out hashes to `1`. Acyclic cores have a single level. The effect is that a hash looks exactly one payload edge into a cycle and no further, so it terminates without any state and gives the same value for a rolled and an unrolled graph.

## 4. Generated file

One file per context, with a hint name made of the context's namespace-qualified name and a short hash. It contains a `partial class` for the context with the following members:

| Member | Shape |
|---|---|
| header | filled from the embedded template `Templates/AutoGeneratedHeader.cs`: an `<auto-generated>` block with the generator version, the context name, its registered roots, every public comparer with its kind, custom comparers, types on the delegate accessor path, the options in effect, the language version and probed capabilities, the diagnostics reported for the context, and documentation links; then `#pragma warning disable CS0612, CS0618, CS8632` plus every custom obsolete, `[Experimental]` and preview-feature id carried by a referenced symbol; then `#nullable enable annotations` when C# 8 or later. Everything comes from the model, so the header is deterministic |
| wrapper, one per named closure type `T` | `public sealed class {T}EqualityComparer : IEqualityComparer<T?>` (structs implement `IEqualityComparer<S>`, with a separate `NullableOf{S}` wrapper); private constructor; `public static readonly Instance`; `Equals` and `GetHashCode` forward to the cores. Accessibility is the narrower of `T`'s effective accessibility and the context's |
| convenience property | `public static {T}EqualityComparer {T} => {T}EqualityComparer.Instance;`, omitted for hazardous names ([DEQ021](Diagnostics.md#registrations)). The context has no static constructor |
| `GetEqualityComparer<T>()` | `public static IEqualityComparer<T>`, backed by a `private static class Cache<T>` whose field is set once from a `Dictionary<Type, object>` through `LookupEqualityComparer`, which never throws and stores null on a miss; `RequireEqualityComparer<T>` then throws `DeepEqualsMissingComparerException` for null. Keys are exact, so `typeof(S)` and `typeof(S?)` are separate entries |
| cores | `private static` methods on the context: `Equals_{T}`, `EqualsExact_{T}`, `EqualsMembers_{T}`, `EqualsBoxed_{S}`, `Equals_SpanOf{T}`, `GetHashCode_{T}`, `ShallowHashCode_{T}`, `GetHashCode_SpanOf{T}` |
| kinds | `private const int Kind_{T}` and `Kind_Boxed_{S}`, one per guarded core |
| constants | `private const int MaxComparisonPairs` and `MaxUnorderedCollisionRun`, from the options |
| accessors | `[UnsafeAccessor(UnsafeAccessorKind.Field, Name = "…")] private static extern ref F {Decl}_{member}(Decl o)` (`ref Decl` for structs); or, on the delegate path, `private static class {Decl}_Accessors` holding `FieldGetter<Decl, F>` delegates created by `DeepEqualsReflection.CreateFieldGetter`; generic declaring types get `Generic_{Def}_Accessors<T…>` mirroring the definition's constraints |
| holders | `private static class {T}_ComparerHolder` with an `internal static readonly Value` typed as the comparer class or `IEqualityComparer<T>`; `{D}_Dispatch` with a `Dictionary<Type, int> CaseIndex` above `MaxSwitchCases`. Every holder has an explicit static constructor so it initializes only on first use |
| ops structs | `private struct {T}Ops` implementing `IDeepEqualsElementOps<T>` or `IDeepEqualsStatelessElementOps<T>`; `{T}ShallowOps : IDeepEqualsHashOps<T>` where a same-component collection hash needs level 0 |
| trimming | delegate holders carry `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]` at class level; cores that use them carry `[UnconditionalSuppressMessage]` for IL2026 and IL3050; the convenience getter of each *unsafe type* (one whose closure reaches a delegate accessor) and, when any exists, `GetEqualityComparer<T>()` carry both attributes; wrappers and the context class carry none. `RequiresDynamicCode` is emitted only where the probe finds the attribute |

Emitted source is C# 7.3: no target-typed `new`, `??=`, `is not`, switch expressions, static local functions or file-scoped namespaces. Every type reference is `global::`-qualified. Every public entry point calls `RuntimeHelpers.EnsureSufficientExecutionStack()`.

Entry point of a wrapper for a type whose closure needs state:

```csharp
public bool Equals(Node? x, Node? y)
{
    RuntimeHelpers.EnsureSufficientExecutionStack();
    DeepEqualsState state = new DeepEqualsState(MaxComparisonPairs);
    try { return Equals_Node(x, y, ref state); }
    finally { state.Dispose(); }
}
public int GetHashCode(Node o) { RuntimeHelpers.EnsureSufficientExecutionStack(); return GetHashCode_Node(o); }
```

Without state the body is a bare `return Equals_Node(x, y);`.

## 5. Naming

Each type gets one short name: its metadata name (`Node`, `Int32`); nested types join with `_` (`Outer_Inner`); generics read as `PairOfStringAndInt32`; nullables as `NullableOfDateOnly`; arrays as `ArrayOfArrayOfByte`; collections use their own metadata name (`ListOfNode`, `IReadOnlyDictionaryOfStringAndNode`). A member segment is the field name, or the property name for compiler storage. A literal `_` and any non-identifier character become `_uXXXX` (`_name` becomes `Node__u005Fname`); keywords get `@`.

When two types claim the same candidate, the generator resolves the clash in this order, only for the claimants: add generic arity (`FooOf2_…`); qualify with the namespace (`Business_Object`); do both; append `__` and the first 16 hex digits of a SHA-256 over the assembly-qualified identity, extending to the full digest if those still collide. A full-digest collision is [DEQ011](Diagnostics.md#naming). A generated name equal to a user-declared member of the context is [DEQ016](Diagnostics.md#context-declaration). Names are frozen in the model, so emission never invents one.

## 6. Cores

Signatures. A core that can reach a cyclic core takes `ref DeepEqualsState state`; others take none. A struct whose estimated size (the sum of its field widths, references counted as 8, an empty struct as 1, no padding) is at most `StructPassByValueMaxByteSize` is passed by value; larger ones by `in`. Hash cores follow the same choice and never take state.

### 6.1 Class

```csharp
private static bool Equals_Node(Node? x, Node? y, ref DeepEqualsState state)
{
    if (ReferenceEquals(x, y)) return true;
    if (x is null || y is null) return false;
    // unsealed: dispatch (6.2)
    if (!state.TryEnter(Kind_Node, x, y)) return true;      // guarded only
    return EqualsMembers_Node(x, y, ref state);
}
```

`EqualsMembers_T` exists as a separate method only when a dispatch core needs to call it; sealed types inline the member chain. The chain is `&&` over the selected members in cost order, split into `if (!(p1 && … && p64)) return false;` blocks of at most `MaxBinaryExpressionArity` predicates; with no members it is `return true;`.

**Tail loop.** A sealed type with a member of its own type compares that member last and turns the recursion into a loop: `while (true) { checks; if (!(members)) return false; x = Next(x); y = Next(y); }`. The null, identity and guard checks repeat on every iteration. Only one member can take the tail position; any other self-typed member recurses normally.

**Inlined structs.** A struct member with at most four members, all leaves read directly, is compared inline (`x.P.X == y.P.X && …`) with no call. `Equals_S` still exists for the wrapper and for dispatch.

### 6.2 Dispatch

For a dispatch type `D` with assignable cases `A1..Am` and exact cases `K1..Kn`:

```csharp
if (ReferenceEquals(x, y)) return true;
if (x is null || y is null) return false;
if (x is A1 ax) { if (!(y is A1 ay)) return false; return <rule A1>(ax, ay); }
if (y is A1) return false;
// … each assignable case, both sides
Type t = x.GetType();
if (t != y.GetType()) return false;
if (t == typeof(D)) { guard; return EqualsMembers_D(x, y, ref state); }   // concrete unsealed D only
if (t == typeof(K1)) return EqualsExact_K1(Unsafe.As<K1>(x), Unsafe.As<K1>(y), ref state);   // guarded K: lands at the guard
if (t == typeof(K2)) return EqualsMembers_K2(Unsafe.As<K2>(x), Unsafe.As<K2>(y), ref state); // unguarded K: lands at the members
throw new DeepEqualsUnknownTypeException(t);
```

Above `MaxSwitchCases` exact cases, the exact chain becomes `switch (D_Dispatch.CaseIndex.TryGetValue(t, out int i) ? i : -1) { case 0: …; default: throw … }`. Both shapes give identical results.

Boxed value cases expose the payload by reference with `Unsafe.Unbox<S>(x)`. A boxed value shape with a cyclic body goes through `EqualsBoxed_S(object x, object y, ref state)`, which guards on the original boxes under `Kind_Boxed_S` and then calls the value body; an acyclic box unboxes directly. Every built-in leaf the compilation exposes is a case of `ObjectEqualityComparer` whether or not anything reached it; enums and user types are cases only when reached. Derived cores compare the full member list, base fields first, through accessors on the declaring type. A static type already covered by a custom or simple rule never dispatches.

### 6.3 Struct

```csharp
private static bool Equals_Point(Point x, Point y) => x.X == y.X && x.Y == y.Y;
private static bool Equals_Large(in Large x, in Large y, ref DeepEqualsState state) { … }
```

No null, identity, guard or runtime-type check. Struct accessors take `ref S`; an `in` core obtains a writable reference through `DeepEqualsHelpers.AsWritableRef(in x)`. Large struct members are passed as `in x.Field`.

### 6.4 Nullable

Inlined at every use site, with no nullable core of its own:

```csharp
x.Opt.HasValue == y.Opt.HasValue && (!x.Opt.HasValue || Equals_Point(x.Opt.GetValueOrDefault(), y.Opt.GetValueOrDefault()))
o.Opt.HasValue ? GetHashCode_Point(o.Opt.GetValueOrDefault()) : 0
```

An exact custom rule for `S?` is resolved before this lowering and receives the nullable value. With only an `S` rule registered, `S?` lowers to it; with only an `S?` rule, an `S` value is wrapped as `new S?(v)`. Each reached or registered `Nullable<S>` gets a wrapper-only node.

### 6.5 Leaves

| Type | Equals | Hash |
|---|---|---|
| `string` | `string.Equals(a, b, StringComparison.Ordinal)` | `DeepEqualsHashCode.Hash(string)` |
| `char`, `bool`, integers of 32 bits or less | `==` | the value |
| `long`, `ulong`, `nint`, `nuint`, `Int128`, `UInt128` | `==` | `Hash(v)` over the 32-bit words |
| `BigInteger`, `Rune` | `==` | `GetHashCode()` |
| `float`, `double`, `Half` | the bits from `SingleToInt32Bits`, `DoubleToInt64Bits` or `HalfToInt16Bits` must match | the same bits (`Hash(long)` for double) |
| `decimal` | the four `GetBits` words must match | `Combine` of the words |
| `Complex`, `Vector2/3/4`, `Quaternion`, `Plane`, `Matrix3x2`, `Matrix4x4`, `PointF`, `SizeF`, `RectangleF` | each float or double component bitwise | `Combine` of the component bits |
| enum | `(U)x == (U)y` for the declared underlying type `U` | `(int)(U)x` for 32 bits or less; `Hash((long)x)` or `Hash((ulong)x)` for 64 bits |
| `Guid` | `==` | `Hash(Guid)` over four words |
| `DateTime` | `DeepEqualsHelpers.DateTimeBits(x)` must match (ticks, kind and the DST flag) | `Hash(ulong)` of that word |
| `DateTimeOffset` | `Ticks` and `Offset` must both match | `Combine(Hash(Ticks), Hash(Offset.Ticks))` |
| `Uri` | ordinal `OriginalString` and `IsAbsoluteUri` | `Combine(Hash(OriginalString), flag)` |
| `TimeSpan`, `DateOnly`, `TimeOnly`, `Index`, `Range`, `Color`, `Point`, `Size`, `Rectangle`, `Version`, `Type`, `IPAddress`, `CultureInfo`, `TimeZoneInfo`, `Encoding` | the default rule below | `GetHashCode()` |
| `Regex` | `ReferenceEquals` ([DEQ022](Diagnostics.md#collections-and-shapes)) | `RuntimeHelpers.GetHashCode` |
| `[SimpleType]` | the default rule below, for the exact static type in use | `GetHashCode()` |
| custom | `T_ComparerHolder.Value.Equals` | `T_ComparerHolder.Value.GetHashCode` |

**Default rule** for a static type `T`, applied after the null and identity checks. It is chosen per exact static type, so a registration such as `[SimpleType(typeof(IInterface))]` only decides which types are covered:

| `T` | Equals |
|---|---|
| implements `IEquatable<T>` for itself, the implementation is a public instance `Equals(T)`, and `T` is a struct or a sealed class | `x.Equals(y)`, a direct call |
| implements `IEquatable<T>` for itself, but the implementation is explicit or `T` is an unsealed class | `DeepEqualsHelpers.EquatableEquals<T>(x, y)`: a constrained call, which is direct and never boxes for a struct and is an interface call for a class |
| anything else, including a `Derived` that only inherits `IEquatable<Base>` | `EqualityComparer<T>.Default.Equals(x, y)` |

All three compute what `EqualityComparer<T>.Default` would for that `T`; the first two skip its comparer lookup and virtual call, which matters on runtimes such as Mono that do not treat the default comparer as an intrinsic.

Reference leaves apply the null and identity rule first. Optional types (`Half`, `Int128`, `DateOnly`, `Rune`, the numerics and drawing types) are leaves only when the compilation exposes them. A leaf is **default-compatible** when its rule agrees with `EqualityComparer<T>.Default`; bitwise floats and their aggregates, `decimal`, `DateTime`, `DateTimeOffset`, `Uri`, `Regex` and custom leaves are not. The model also records whether `T` implements `IEquatable<T>` in the referenced assemblies.

The custom holder field is typed as the comparer class only when it is a class to which the acquisition expression converts and both `Equals(T, T)` and `GetHashCode(T)` bind to its public implementing methods; otherwise it is typed `IEqualityComparer<T>`. With `handleNulls: false` the call is wrapped in the null rule; with `true` it is called bare.

### 6.6 Products and memory

`KeyValuePair<K,V>`: `Key` under `K` and `Value` under `V`, hash `Combine(keyHash, valueHash)`. `ValueTuple` and `Tuple`: items in order, `Rest` flattened when it is itself a tuple; reference tuples take the null and identity rule and a guard when cyclic. `Memory<T>` and `ReadOnlyMemory<T>`: `Length` must match, then `.Span` goes into the span core. `Span<T>` and `ReadOnlySpan<T>` cannot be registered ([DEQ010](Diagnostics.md#registrations)) or be members ([DEQ012](Diagnostics.md#members)).

### 6.7 Ordered collections

| Static type | Equals | Hash |
|---|---|---|
| `T[]` | identity, null, `Length`, guard, span core | `HashSpan` or `Streaming` over the elements |
| `List<T>` | the same via `CollectionsMarshal.AsSpan`, else an indexer loop | the same |
| `ImmutableArray<T>` | `IsDefault` check first (both default: true; one default: false), then `AsSpan()` or the indexer | default gives 0, else the contents |
| `ArraySegment<T>` | `Count`, then the slice via a span or `Array[Offset + i]` | the slice contents |
| `IReadOnlyList<T>`, `IList<T>` | identity, null, `ImmutableArray` check, `Count`, guard, then `TryGetSpan` on each side independently, else an `x[i]` loop through the interface | the same source |
| `IReadOnlyCollection<T>`, `IEnumerable<T>` | the same, using counts where available, else two enumerators walked together; equal nonzero counts are checked against what enumeration yields (`InvalidOperationException` on a mismatch) | a span, or `Streaming` by enumeration |

**Span core.** `Equals_SpanOf{T}(ReadOnlySpan<T> xs, ReadOnlySpan<T> ys[, ref state])` owns the length check. A default-compatible leaf that implements `IEquatable<T>` uses `xs.SequenceEqual(ys)`; enums reinterpret through `MemoryMarshal.Cast` where available; everything else loops and calls the element rule. Every span path takes `ReadOnlySpan<T>`, so a `Derived[]` held in a `Base[]` variable cannot throw. Interface cores cast once to the selected interface instantiation and read `Count`, the indexer and the enumerator through it. Subclasses of `T[]`, `List<T>`, `HashSet<T>` and `Dictionary<K,V>` take the base type's fast path. The advertised `Count` is trusted for early decisions. Struct collections other than `ImmutableArray<T>` and `ArraySegment<T>` are boxed once per side on directly typed paths.

### 6.8 Unordered collections

```csharp
private static bool Equals_ISetOfNode(ISet<Node>? x, ISet<Node>? y, ref DeepEqualsState state)
{
    if (ReferenceEquals(x, y)) return true;
    if (x is null || y is null) return false;
    int count = x.Count;
    if (count != y.Count) return false;
    if (!state.TryEnter(Kind_ISetOfNode, x, y)) return true;                 // guarded only
    return DeepEqualsUnordered.SetEquals<Node, NodeOps>(x, y, count, MaxUnorderedCollisionRun, ref state);
}
```

Dictionaries call `DictionaryEquals<K, V, KeyValuePairOfKAndVOps>`. When the entry type's closure is acyclic, the stateless overloads and `IDeepEqualsStatelessElementOps<T>` are used instead. The hash is `Combine(Count, sum of entry hashes)` with the empty rule applied.

### 6.9 Unordered algorithm

`DeepEqualsUnordered.SetEquals` and `DictionaryEquals<…, TOps>` take `TOps` as a struct, so the JIT specializes the routine and every callback into generated code is a direct call:

1. **Materialize** both sides into rented `T[]` entry arrays and rented `long[]` keys packed as `(hash << 32 | index)`, hashing each entry once through `TOps` and keeping an unchecked running sum per side. A `HashSet<T>` or `Dictionary<K,V>` is walked with its struct enumerator; anything else through `IEnumerable<T>`, checking the number of yielded entries against the captured `Count`. If the two sums differ, return `false`.
2. **Sort** each key array. Ties keep enumeration order, so the result is deterministic.
3. **Merge-walk** the two sorted arrays run by run, where a run is a group of entries sharing one hash. A hash present on only one side, or runs of different length, means `false`. For a run of length 1, one `Equals` call decides: `true` keeps whatever triples it entered, `false` is terminal. For a run of length `k` greater than 1 (`k` at most `MaxUnorderedCollisionRun`, otherwise `DeepEqualsComplexityException`): build a `k` by `k` compatibility matrix as a rented bitset, wrapping each trial in `mark = state.Mark(); …; state.Rollback(mark)` whether it returned `true` or `false`; find a perfect matching with an iterative augmenting-path search (Kuhn's algorithm) over a rented workspace; no perfect matching means `false`; then run `Equals` once more on each matched pair without rollback so their triples are kept.
4. Every run matched means `true`.

Every rented array is acquired inside a try/finally that begins before the first rent and is returned exactly once, with reference-bearing arrays cleared. A fast path applies when both operands are `HashSet<T>`, both have `Comparer` reference-equal to `EqualityComparer<T>.Default`, and `T` is default-compatible: then `x.SetEquals(y)` is the answer. There is no dictionary fast path.

The exact matching, rather than a greedy first-fit, is what makes unordered equality symmetric and independent of enumeration order under coinduction: a trial may succeed only because it assumed an in-progress ancestor pair, and greedy pairing could then strand a later entry that a different pairing would have matched.

## 7. Runtime state

`DeepEqualsState` is a `ref struct` declared in the entry point and passed by `ref`. It holds an inline buffer of 8 `ReferencePair(kind, x, y)` entries (an `[InlineArray]` on net8.0 and later, eight explicit fields below), a count, and the pair budget. Once the buffer is full it spills into a rented `ReferencePair[]` journal in insertion order plus a rented `int[]` open-addressing index whose slots store pair index plus one (0 means empty) at a load factor of at most one half. Logical capacities are tracked separately from the rented array lengths: the initial pair capacity is the smaller of 32 and the budget, doubling up to the budget, and the index is the next power of two at or above twice the pair capacity.

| Operation | Behaviour |
|---|---|
| `TryEnter(kind, x, y)` | `EnsureSufficientExecutionStack()`; scan the inline buffer (at most 8) or probe the index; if found, return `false`; if the count equals the budget, throw `DeepEqualsComplexityException`; otherwise insert and return `true`. A hit never consumes budget or grows storage. The inline half is `AggressiveInlining`; spill, grow and probe are the slow half |
| `Mark()` | returns the current count |
| `Rollback(mark)` | removes the pairs added since the mark in reverse insertion order, clearing their index slots, so no tombstones are needed |
| `Dispose()` | returns the rented arrays, clearing the pairs |

`ReferencePair.GetHashCode` mixes `RuntimeHelpers.GetHashCode` of both objects and the kind with fixed constants, independently of the seeded value hash. Spill and grow acquire the replacement arrays into locals under try/finally, copy and reindex in journal order, then publish; if a rent fails, everything acquired so far is returned and the old state stays valid. Capacity arithmetic is checked and done in wide integers; `MaxComparisonPairs` of at most 2^29 keeps the index at most 2^30 entries and the plus-one encoding inside `int`. The pool is `DeepEqualsPools<T>.Shared`, which is `ArrayPool<T>.Shared` and replaceable by tests.

Guarded cores are: cyclic deep class bodies, cyclic reference products and tuples, cyclic reference collections, and boxed-value adapters. Dispatch cores and unboxed struct bodies are never guarded. State is needed only when a cyclic core, or an unordered collection whose entry closure is cyclic, is reachable.

## 8. Hash runtime

`DeepEqualsHashCode` is a static class implementing xxHash32 exactly as `System.HashCode` does, with a seed drawn once per process from `RandomNumberGenerator` and every operation `unchecked`:

- `Combine(int v1, …, int vN)` for `N` from 1 to 32, each straight-line with no loop, generated into `DeepEqualsHashCode.Combine.g.cs`; arities up to 3 are `AggressiveInlining`. Generated code nests calls above 32 inputs.
- `Hash(long)`, `Hash(ulong)`, `Hash(Int128)`, `Hash(UInt128)`, `Hash(Guid)`: one seeded `Combine` over the value's 32-bit words. `Hash(string?)`: 0 for null; `string.GetHashCode()` on assets whose runtime randomizes it; a seeded Marvin32 in the netstandard2.0 asset.
- `HashSpan<T, TOps>(ReadOnlySpan<T>)` in the netstandard2.1 and later assets: the xxHash32 stream, unrolled four elements at a time, over element hashes obtained through `TOps`.
- `Streaming`, a `ref struct` on every asset, with `Add(int)` and `ToHashCode()`, the same stream as `System.HashCode.Add`.
- `HashSpan`, `Streaming` and `Combine(h1..hn)` agree for 1 to 32 inputs. Both streaming forms and the generated unordered hashes apply the empty rule.

`System.HashCode` is not used on any tier. There is no per-object hash caching.

## 9. Member selection and access

Roslyn exposes synthesized storage directly: `<Name>k__BackingField` for auto, positional-record and `field`-keyword properties and `<name>P` for captured primary-constructor parameters, each marked `IsImplicitlyDeclared` and, for property storage, carrying `AssociatedSymbol`. Types read from metadata are recognized by `[CompilerGenerated]` plus the same name patterns. A record's `EqualityContract` is a computed property and is skipped.

The transform reads the compilation through a `MetadataImportOptions.All` view (`compilation.WithOptions(...)`, cached per compilation in a `ConditionalWeakTable`), because the default `Public` import hides the private fields of referenced types. Types from assemblies carrying `ReferenceAssemblyAttribute`: deep classes and structs with only `_dummy*` placeholders are [DEQ017](Diagnostics.md#referenced-assemblies); other structs are [DEQ026](Diagnostics.md#referenced-assemblies) and use their visible fields; leaves and shapes are unaffected.

How each selected field is read:

| Field | Read |
|---|---|
| accessible from the context | directly, `x.F` |
| otherwise, when `UnsafeAccessor` is available (core library `System.Runtime` version 8 or later) | `[UnsafeAccessor(Field, Name = "…")] static extern ref F Decl_F(Decl o)`, resolved by the JIT into a direct field access |
| generic declaring type, core library version 9 or later | the same accessor inside `Generic_{Def}_Accessors<T…>`, whose type parameters and constraints mirror the definition (`notnull` omitted); falls back to delegates if a constraint names an inaccessible type |
| otherwise | a `FieldGetter<Decl, F>(ref Decl)` delegate built once by `DeepEqualsReflection.CreateFieldGetter` from an expression tree with a by-ref receiver, in a lazy holder; struct, nullable and value-shape reads are hoisted into a local so the delegate runs once per member |

Getters are never invoked. A virtual auto-property overridden with a computed getter compares the base storage; an abstract property contributes no base storage. Volatile fields are read as plain fields. Runtime capability is decided by the identity and version of the assembly that defines `System.Object`, never by whether an attribute type happens to exist.

## 10. Type graph analysis

Done once per context in the transform and stored in an immutable, equatable model:

1. **Closure**, per [§1](#1-vocabulary), checking cancellation at every type and enforcing the [DEQ018](Diagnostics.md#registrations) bounds.
2. **Nodes**: one per core that will be emitted: `Equals_D` for each dispatch type, `EqualsMembers_T` for each body, one per product, memory and collection core, and a boxed adapter node between a dispatch core and each boxed value body.
3. **Edges**: body to the core of each selected member's resolved strategy, with nullables lowered to their payload; product to its items; collection to its element, or key and value; dispatch to the body of each exact case (including its own body when concrete), to each boxed adapter and on to the value body, and to each leaf or shape case; and boxed-value edges reached through collection interface paths.
4. **Strongly connected components** by Tarjan's algorithm. A core is cyclic when its component has more than one node or it has a genuine self edge. Every cyclic core that works on references gets a guard; dispatch cores and unboxed value bodies do not.
5. **State**: a core takes `ref state` when it can reach a cyclic core, or an unordered collection whose entry closure is cyclic. This decides entry-point shapes and whether unordered calls use the stateful or stateless overloads.
6. **Hash levels**, per [§3](#3-the-hash-function), which also selects full or shallow ops structs at each collection hash site.
7. **Member order**, `MaxBinaryExpressionArity` chunks, struct size estimates, the accessor list, dispatch case order, the tail member, inlined-struct decisions, and the identifier table with its collision resolution.

Emission is then a straight walk of the model with no decisions left to make.

## 11. Target capabilities

The generator never reads the target framework name. API capabilities are probed with `GetTypesByMetadataName`, taking the first accessible non-error candidate, and framework overloads are probed on the framework asset the consumer actually resolved. Runtime capabilities come from the core library's identity; language features from `ParseOptions.LanguageVersion`. A multi-targeting consumer runs the generator once per target.

| Capability | Present on | Without it |
|---|---|---|
| `ReadOnlySpan<T>`, `Memory<T>` | netstandard2.1 and later, or netstandard2.0 with `System.Memory` | indexer and enumerator loops; no memory shapes |
| `DeepEqualsCollections.TryGetSpan`, `DeepEqualsHashCode.HashSpan` | framework assets netstandard2.1 and later | interface views use indexers or enumerators; hashes use `Streaming` |
| `CollectionsMarshal.AsSpan` | .NET 5 and later | `List<T>` indexer loop; `TryGetSpan` recognizes arrays only |
| `IReadOnlySet<T>` | .NET 5 and later | the `ISet<T>` shape |
| `Nullable.GetValueRefOrDefaultRef` | .NET 7 and later | `GetValueOrDefault()` (not yet used, see [§14](#14-not-implemented)) |
| `decimal.GetBits(decimal, Span<int>)` | .NET 8 and later | the allocating `GetBits(decimal)` |
| `BitConverter.SingleToInt32Bits` | netstandard2.1 and later | `DeepEqualsHelpers.SingleToInt32Bits` |
| `[UnsafeAccessor]` | core library version 8 and later | expression-tree delegates |
| `[UnsafeAccessor]` on generic declaring types | core library version 9 and later | delegates for those fields |
| `RequiresDynamicCodeAttribute` | in-box from .NET 7; public in the netstandard assets; internal in the net6.0 asset | the attribute is omitted |
| `Half`, `Int128`, `DateOnly`, `TimeOnly`, `Rune`, `Index`, `Range`, numerics, drawing | when referenced | not leaves |
| nullable annotations | C# 8 and later | omitted |

Framework asset builds: netstandard2.0 (`System.Buffers` and `System.Runtime.CompilerServices.Unsafe` packages, eight-field state buffer, Marvin, `Streaming` only), netstandard2.1 (`Unsafe` package, `TryGetSpan`, `HashSpan`), net6.0 (in-box `BitOperations` and trimming attributes), net8.0 and net10.0 (`InlineArray`). Generated code differs between net8.0 and net10.0 only in the generic-type accessors.

## 12. Pipeline

```
ForAttributeWithMetadataName("DeepEquals.SourceGeneration.GenerateDeepEqualsAttribute")            → ContextModel?
ForAttributeWithMetadataName("DeepEquals.SourceGeneration.DeepEqualsSourceGenerationOptionsAttribute") → ContextModel?
RegisterSourceOutput(each, Emit)         // per context, no Collect, no CompilationProvider
```

A context split across partial declarations belongs to whichever provider's attribute appears on the first marked declaration in file-path-then-position order; the other provider returns null for it. Cheap validation of the declaration form and options runs before closure construction. The model contains no `ISymbol`, `Compilation`, `SyntaxNode` or `Location`; diagnostics are stored as `(id, file, span, line span, args)` records; a model equal to the previous run skips emission. An unexpected exception in the transform or in emission becomes [DEQ099](Diagnostics.md#generator-failure) at the canonical declaration; `OperationCanceledException` is rethrown. Emission writes into one `StringBuilder` sized at the smaller of `types × 2048` and 4 MB.

## 13. Allocation contract

After first-use initialization (wrappers, holders, delegates, dispatch maps, `Cache<T>`):

| Situation | Allocation |
|---|---|
| acyclic graph; arrays, `List<T>`, `ImmutableArray<T>`, `ArraySegment<T>`, memory shapes | none |
| `decimal` below net8.0 | `Equals` two `int[4]`, `GetHashCode` one |
| cyclic graph with at most 8 retained triples | none |
| cyclic graph beyond 8 triples; any set or dictionary comparison | rented arrays; a pool miss allocates |
| set or dictionary interface holding a `HashSet<T>` or `Dictionary<K,V>` | struct enumerator, nothing further |
| `IReadOnlyList<T>` or `IList<T>` holding a `T[]` or `List<T>` | none |
| `IEnumerable<T>` or `IReadOnlyCollection<T>` holding a known non-span collection | one boxed enumerator per side |
| an interface holding an unrecognized implementation | whatever its `GetEnumerator` allocates |
| a struct covered by an interface custom rule, on a directly typed path | one box per side |
| a spilled state | `RuntimeHelpers.GetHashCode` touches object headers; no managed allocation |
| near the default pair budget | about 24 MB of pairs plus an 8 MB index, pooled; pools before .NET 6 may not retain the 2^21-entry index |

## 14. Not implemented

- `Nullable.GetValueRefOrDefaultRef`; payloads are read with `GetValueOrDefault()`.
- Small-struct inlining when any member needs an accessor.
- [DEQ009](Diagnostics.md#custom-comparers-and-simple-types); unrelated covering interface comparers report [DEQ031](Diagnostics.md#custom-comparers-and-simple-types) instead.
- Canonical collection cases behind a dispatch type exist only for element types that some reached container has.
- Multi-dimensional arrays ([DEQ014](Diagnostics.md#collections-and-shapes)); `fixed` buffers and `[InlineArray]` structs as spans ([DEQ032](Diagnostics.md#members)); `[DeepEqualsInclude]` for computed properties; a dictionary `TryGetValue` fast path; cost-based member ordering; partitioned output for very large closures; automatic derived-type discovery; vectorized `Combine`.
