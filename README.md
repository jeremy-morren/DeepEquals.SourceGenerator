# DeepEquals.SourceGenerator

A Roslyn source generator that emits deep, by-value `IEqualityComparer<T>` implementations for a closed set of types. 
Generated comparison cores are static, cycle-safe, compare instance storage rather than property getters, 
and avoid steady-state allocations wherever the runtime allows.

- [Installation](#installation)
- [Project setup](#project-setup)
- [Usage](#usage)
- [Attributes](#attributes)
- [Options](#options)
- [What is compared](#what-is-compared)
- [Exceptions and limits](#exceptions-and-limits)
- [Trip hazards](#trip-hazards)
- [Diagnostics](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Diagnostics.md): every `DEQ` id, when it fires and how to fix it
- [Implementation](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Implementation.md): the equality relation, the generated code and the runtime library

## Installation

```xml
<PackageReference Include="DeepEquals.SourceGenerator" Version="1.0.0-beta01" PrivateAssets="all" />
<PackageReference Include="DeepEquals.SourceGeneration.Framework" Version="1.0.0-beta01" />
```

Two packages, in every project that declares a context. `DeepEquals.SourceGenerator` is the generator, an analyzer with no runtime surface of its own. 
`DeepEquals.SourceGeneration.Framework` is the library the generated code compiles and runs against, for `netstandard2.0`, `netstandard2.1`, `net6.0`, `net8.0` and `net10.0`. 
The consuming project must be C# and compile with Roslyn 4.3.1 or later (.NET SDK 6.0.400, Visual Studio 17.3). Generated source is C# 7.3-compatible; nullable annotations appear from C# 8.

**`PrivateAssets="all"` on the generator keeps it to the project that declares it.** Leaving the flag off is not an error;
it means every project downstream of yours also loads the generator. It emits nothing where no context is declared, so the cost is only analyzer load time.

Everything under [Project setup](#project-setup) applies to the project that **declares the context**.

| Consumer target                   | Runtime asset  | Notes                                                                                                                             |
|-----------------------------------|----------------|-----------------------------------------------------------------------------------------------------------------------------------|
| `netstandard2.0`, `net472`        | netstandard2.0 | `System.Buffers` and `System.Runtime.CompilerServices.Unsafe` flow transitively. Field access below net8.0 uses cached delegates. |
| `netstandard2.1`, `netcoreapp3.1` | netstandard2.1 | `System.Runtime.CompilerServices.Unsafe` flows transitively.                                                                      |
| `net6.0`, `net7.0`                | net6.0         | No extra dependencies. Compiles and runs; not a run-time test tier.                                                               |
| `net8.0`                          | net8.0         | `[UnsafeAccessor]` field access; generic declaring types still use delegates.                                                     |
| `net10.0`                         | net10.0        | `[UnsafeAccessor]` for every field, including generic declaring types.                                                            |

`net5.0` and earlier .NET Core versions are unsupported as direct consumers (the netstandard2.1 asset's trimming attributes conflict with the in-box ones, CS0433). 
A `netstandard2.1` library using this package still runs on them.

## Project setup

**Same project.** When the context and the types it compares live in one project, nothing is needed beyond the package reference.

**Types in another project.** A `ProjectReference` normally hands the compiler a reference assembly, from which private fields and auto-property backing fields are stripped. 
The generator then reports [`DEQ017`](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Diagnostics.md#referenced-assemblies) for every class it would have to walk. 
Set the MSBuild property `CompileUsingReferenceAssemblies` to `false` in the project that **declares the context**:

```xml
<PropertyGroup>
  <CompileUsingReferenceAssemblies>false</CompileUsingReferenceAssemblies>
</PropertyGroup>
<ItemGroup>
  <ProjectReference Include="../Models/Models.csproj" />
</ItemGroup>
```

The property has no effect on a `PackageReference` whose package ships a `ref/` assembly; register such types with `[SimpleType]` or `[CustomEqualityComparer]` instead.

**Internal types in another project.** A member whose type is `internal` to another assembly is [`DEQ003`](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Diagnostics.md#members). 
Add `InternalsVisibleTo` for the context's assembly in the declaring project, or ignore the member.

**Projects targeting `net472` or later** The transitive `System.Runtime.CompilerServices.Unsafe` package needs binding redirects:

```xml
<PropertyGroup>
  <AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>
  <GenerateBindingRedirectsOutputType>true</GenerateBindingRedirectsOutputType>
</PropertyGroup>
```

**`netstandard2.0` with spans.** Referencing `System.Memory` lets the generator emit `Memory<T>`/`ReadOnlyMemory<T>` shapes and direct span equality loops. 
Hash loops and interface-typed collection views still use streaming and indexer/enumerator fallbacks on that tier.

**Trimming and NativeAOT.** Field access through `[UnsafeAccessor]` is trim- and AOT-safe. Where the delegate fallback is needed (below net8.0, and generic declaring types on net8.0), 
the affected type's convenience property getter and `GetEqualityComparer<T>()` carry `[RequiresUnreferencedCode]`/`[RequiresDynamicCode]`, 
so the warning appears on access of that type and nowhere else. Safe types in the same context produce no warning.

**Warnings as errors.** Generated files disable `CS0612`, `CS0618`, `CS8632` and every custom obsolete, `[Experimental]` and preview-feature diagnostic id carried by a type they reference.

## Usage

```csharp
using DeepEquals.SourceGeneration;

[GenerateDeepEquals(typeof(Customer))]
[GenerateDeepEquals(typeof(Order))]
public partial class MyDeepEqualsContext : DeepEqualsContextBase { }

bool same = MyDeepEqualsContext.Customer.Equals(a, b);
int hash  = MyDeepEqualsContext.Customer.GetHashCode(a);
IEqualityComparer<Order> orders = MyDeepEqualsContext.GetEqualityComparer<Order>();
```

The context must be a non-generic, non-abstract `partial` class deriving from `DeepEqualsContextBase`.

Registering a type registers its closure: member types, element/key/value types, tuple items, base classes and implemented interfaces.
Every type in the closure gets:
- a nested `{Name}EqualityComparer` wrapper with a singleton `Instance`,
- a static convenience property `MyDeepEqualsContext.{Name}`,
- an entry behind `GetEqualityComparer<T>()`, which throws `DeepEqualsMissingComparerException` for a type outside the closure.

Derived types are **not** discovered. Register every runtime type a polymorphic member (`object`, an interface, an abstract or unsealed class) can hold; 
an unregistered runtime type throws `DeepEqualsUnknownTypeException` at comparison time.

**Shared registrations.** Attributes on an abstract base context are inherited. A derived context must carry at least one 
`[GenerateDeepEquals]` or a `[DeepEqualsSourceGenerationOptions]` on its own declaration to be discovered:

```csharp
[GenerateDeepEquals(typeof(Customer))]
[SimpleType(typeof(Sku))]
public abstract class SharedContext : DeepEqualsContextBase { }

[GenerateDeepEquals(typeof(Invoice))]        // adds a type
public partial class BillingContext : SharedContext { }

[DeepEqualsSourceGenerationOptions]          // adds nothing; marks the class as a context
public partial class ReportingContext : SharedContext { }
```

Deriving one concrete context from another is unsupported.

## Attributes

All attributes live in `DeepEquals.SourceGeneration`.

| Attribute                                                                        | Target                                                                                 | Meaning                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
|----------------------------------------------------------------------------------|----------------------------------------------------------------------------------------|-------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `[GenerateDeepEquals(typeof(T))]`                                                | context, repeatable                                                                    | Registers `T` as a closure root. Only a root marker: it never overrides a simple or custom rule for `T`. Registering both a base and a derived type equals registering the derived type.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                  |
| `[DeepEqualsSourceGenerationOptions(...)]`                                       | context, once                                                                          | Per-context options (below). An empty attribute also marks a derived context. Options merge per property along the base chain; derived wins.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                              |
| `[SimpleType(typeof(T))]`                                                        | context, repeatable                                                                    | `T` and every type assignable to it is a leaf using its own default equality for the static type in use: a direct `Equals(TStatic)` call when `TStatic` implements `IEquatable<TStatic>` itself, otherwise `EqualityComparer<TStatic>.Default`. Registering an interface such as `IInterface` covers every implementing struct or class. `typeof(S?)` for a struct normalizes to `S` ([`DEQ028`](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Diagnostics.md#custom-comparers-and-simple-types)). A `Base` and a `Derived` rule overlap; the narrower is ignored ([`DEQ025`](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Diagnostics.md#custom-comparers-and-simple-types)). |
| `[CustomEqualityComparer(typeof(TComparer), handleNulls = false)]`               | context, repeatable                                                                    | Compares `T`, taken from `TComparer`'s single `IEqualityComparer<T>`, with that comparer everywhere `T` or an assignable type appears. The instance comes from a public static `Instance` or `Default` member, else a public parameterless constructor.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| `[CustomEqualityComparer(typeof(TComparer), "MemberName", handleNulls = false)]` | context, repeatable                                                                    | Same, taking the instance from a named public static field, property or parameterless method. Abstract comparer classes are allowed: `[CustomEqualityComparer(typeof(StringComparer), nameof(StringComparer.OrdinalIgnoreCase))]`.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| `[DeepEqualsIgnore]`                                                             | field, auto-property, `field`-keyword property, captured primary-constructor parameter | Excludes that storage from comparison and hashing.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                        |
| `[DeepEqualsIgnore(typeof(Declaring), "member")]`                                | context, repeatable                                                                    | Excludes storage you cannot annotate, such as a private field of a base class in another assembly. The name resolves as a field first, otherwise as a property whose backing field is excluded.                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                           |

Custom comparer rules:

- `T` may be any type, including a constructed collection such as `Dictionary<string, int>` or `IReadOnlyDictionary<string, int>`; matching is instantiation-exact, with ordinary assignability (a comparer for an interface covers its implementations, including structs, which are boxed once per side).
- With `handleNulls: false` the library null rule runs first and the comparer sees only non-null values. With `handleNulls: true` the comparer receives null and owns the whole contract.
- `T` and `T?` registrations for a struct are distinct. With only `T` registered, `T?` checks `HasValue` and delegates. With only `T?` registered, `T` is wrapped and delegated without boxing.
- A custom rule beats a covering simple rule ([`DEQ008`](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Diagnostics.md#custom-comparers-and-simple-types)). A `Base` rule covers `Derived`; registering both ignores `Derived` ([`DEQ030`](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Diagnostics.md#custom-comparers-and-simple-types)). Two unrelated interface rules covering one type is an error ([`DEQ031`](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Diagnostics.md#custom-comparers-and-simple-types)). A rule for `object` is ignored ([`DEQ035`](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Diagnostics.md#custom-comparers-and-simple-types)).
- Comparers and their factories must be thread-safe and return stable results throughout an operation. They are resolved lazily, once per process.

## Options

Set on `[DeepEqualsSourceGenerationOptions]`. Invalid values warn ([`DEQ013`](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Diagnostics.md#options-and-bounds)) and fall back to the default.

| Option                         | Default   | Range              | Effect                                                                                                                                                                                              |
|--------------------------------|-----------|--------------------|-----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `MaxSwitchCases`               | 12        | ≥ 1                | Exact dispatch cases above which a polymorphic core switches from an `if` chain to a `Dictionary<Type, int>` lookup and `switch`.                                                                   |
| `MaxUnorderedCollisionRun`     | 64        | 1..512             | Longest run of equal-hash entries a set or dictionary comparison resolves by exact matching; longer runs throw.                                                                                     |
| `MaxComparisonPairs`           | 1,000,000 | 1..2^29            | Distinct `(kind, left, right)` triples one comparison may retain; the next novel triple throws. Bounds memory as well as cyclic work.                                                               |
| `MaxBinaryExpressionArity`     | 64        | ≥ 1                | Largest generated `&&` chain; longer member lists are split into consecutive `if` statements.                                                                                                       |
| `StructPassByValueMaxByteSize` | 8         | ≥ 0                | Structs whose estimated field size is at most this are passed by value to generated cores; larger ones by `in`.                                                                                     |
| `ExcludeInterfacesByPrefix`    | empty     | namespace prefixes | Interfaces whose namespace equals a prefix or starts with `prefix + "."` are skipped by the automatic base/interface crawl, as `System` already is. Does not affect explicit roots or member types. |

## What is compared

- **Storage, not getters.** Every instance field, public or private, including auto-property, `field`-keyword and primary-constructor capture storage, across the whole base chain. Computed properties, static fields, delegates and events are not compared. Selected model getters are never invoked.
- **Strings** ordinally. **`char`** and **`bool`** by value.
- **Floating point** bitwise: `NaN` equals only the same `NaN` bits, `+0` differs from `-0`. The same rule applies inside `Complex`, the `System.Numerics` vectors and matrices, and `PointF`/`SizeF`/`RectangleF`.
- **`decimal`** by its four representation words, so `1.0m` differs from `1.00m` and `0m` from `-0m`.
- **`DateTime`** by its complete 64-bit storage: ticks, kind and the hidden ambiguous-daylight-saving flag. **`DateTimeOffset`** by ticks and offset. **`Uri`** by ordinal `OriginalString` plus the absolute/relative flag.
- **Enums** as their declared underlying integer, all bits. **64-bit and wider leaves**, `Guid` and strings hash through a per-process seeded mix rather than the BCL's xor fold.
- **Other built-in leaves** (`Guid`, `TimeSpan`, `DateOnly`, `TimeOnly`, `Version`, `Type`, `IPAddress`, `CultureInfo`, `TimeZoneInfo`, `Encoding`, `Index`, `Range`, `BigInteger`, `Half`, `Int128`, `Rune`, `Color`, `Point`, `Size`, `Rectangle`) and `[SimpleType]` leaves by their own equality, calling `IEquatable<T>.Equals` directly where the type implements it. **`Regex`** by reference, with [`DEQ022`](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Diagnostics.md#collections-and-shapes) unless a custom comparer is registered.
- **Structs** by members, ignoring layout and padding. **Nullable values** by `HasValue` then payload.
- **`KeyValuePair<K,V>`**, **`ValueTuple`** and **`Tuple`** item by item; tuple element names do not participate.
- **Ordered collections** (`T[]`, `List<T>`, `ImmutableArray<T>`, `ArraySegment<T>`, `Memory<T>`, `IList<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `IEnumerable<T>`) element by element, by the **declared** static type. A `HashSet<T>` behind `IEnumerable<T>` is an ordered sequence.
- **Sets** (`ISet<T>`, `IReadOnlySet<T>`) and **dictionaries** (`IDictionary<K,V>`, `IReadOnlyDictionary<K,V>`) as unordered multisets under **this library's** equality of elements and key-value pairs. The collection's own comparer is never consulted.
- **Polymorphic members** (`object`, interfaces, abstract or unsealed classes) require equal runtime types, then dispatch to the concrete type's comparison. Collections and leaves behind such a member match by shape rather than exact runtime type, so `HashSet<T>` equals `SortedSet<T>` behind `ISet<T>`.
- **Cycles** terminate with coinductive semantics: a pair already under comparison is assumed equal, so `A→B→A` equals its unrolled form. Shared and copied subgraphs compare equal, and equal graphs always hash equal.
- **Null and empty** collections differ; null hashes to 0, empty to a nonzero value.

Register a custom comparer for any type where the BCL's normalized equality is what you want.

## Exceptions and limits

| Exception                             | When                                                                                                                                                                                  |
|---------------------------------------|---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `DeepEqualsUnknownTypeException`      | A polymorphic member holds a runtime type outside the closure, on both sides with the same type, or on any hash. Two *different* unregistered types compare unequal without throwing. |
| `DeepEqualsMissingComparerException`  | `GetEqualityComparer<T>()` for a `T` outside the closure.                                                                                                                             |
| `DeepEqualsComplexityException`       | `MaxComparisonPairs` exceeded, or a set/dictionary hash-collision run longer than `MaxUnorderedCollisionRun`.                                                                         |
| `InsufficientExecutionStackException` | Recursion deep enough to threaten the thread stack; checked at every entry point and recursive core.                                                                                  |
| `InvalidOperationException`           | A collection enumerated more or fewer elements than its advertised `Count`.                                                                                                           |

Neither budget bounds cumulative work or elapsed time; nested unordered trials may repeat comparisons. Allocation-free steady state holds for acyclic graphs and known collections; cyclic graphs beyond 8 retained pairs and every set/dictionary comparison rent pooled arrays, and exact `decimal` below net8.0 allocates small arrays. Near the default pair budget a spilled state can hold roughly 32 MB of pooled arrays.

## Trip hazards

Deliberate limits. The library compares plain object graphs it can see completely; anything else is solved with `[CustomEqualityComparer]`.

- **Collection comparers are ignored.** Two case-insensitive `Dictionary<string, int>` holding `{"A": 1}` and `{"a": 1}` are unequal. Register a comparer for the key type, or for the whole constructed dictionary type.
- **The declared view is the semantics.** Changing a member from `ISet<T>` to `IEnumerable<T>` changes it from unordered to ordered.
- **Subclasses of BCL collections** are compared as their base storage; a collection-shaped user type with extra fields is compared as the collection ([`DEQ027`](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Diagnostics.md#collections-and-shapes)).
- **Lazy or single-use enumerables** may be enumerated separately for equality and hashing ([`DEQ004`](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Diagnostics.md#collections-and-shapes)).
- **Unknown runtime types** are detected only for equal-typed pairs and hashes; do not rely on the exception to find a missing registration.
- **`ImmutableArray<T>` behind a covariant view** (`ImmutableArray<string>` as `IReadOnlyList<object>`) is not recognized; a default value may throw on `Count`.
- **Fixed buffers and `[InlineArray]` structs** are rejected ([`DEQ032`](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Diagnostics.md#members)); **multi-dimensional arrays** are rejected ([`DEQ014`](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Diagnostics.md#collections-and-shapes)); **`dynamic`** members are ignored ([`DEQ023`](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Diagnostics.md#members)).
- **Reference-assembly types** cannot be walked ([`DEQ017`](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Diagnostics.md#referenced-assemblies), [`DEQ026`](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Diagnostics.md#referenced-assemblies)); see [Project setup](#project-setup).
- **Structs covered by an interface custom rule** are boxed once per side so the direct and boxed paths agree.
- **Concurrent mutation** during a comparison, code weaving and proxies are unsupported.
- **Partial BCL polyfills** (a `System.Half` without `BitConverter.HalfToInt16Bits`) fail with an ordinary compile error in generated code.
