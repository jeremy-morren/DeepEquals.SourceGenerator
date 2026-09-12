# Diagnostics

Every diagnostic the generator reports uses the `DEQ` prefix and the analyzer category `DeepEquals`. An **error** stops generation for that context: no source is added, and the first use of `MyContext.T` or `GetEqualityComparer<T>()` fails to compile. A **warning** leaves the context generating with the documented fallback. There is no strict mode: a warning you choose to ignore is your decision.

Silence a warning with `<NoWarn>DEQ004</NoWarn>` in the project file or `dotnet_diagnostic.DEQ004.severity = none` in `.editorconfig`.

- [Context declaration](#context-declaration)
- [Registrations](#registrations)
- [Custom comparers and simple types](#custom-comparers-and-simple-types)
- [Members](#members)
- [Collections and shapes](#collections-and-shapes)
- [Referenced assemblies](#referenced-assemblies)
- [Naming](#naming)
- [Options and bounds](#options-and-bounds)
- [Generator failure](#generator-failure)

## Context declaration

| Id | Severity | When | Effect | Fix |
|---|---|---|---|---|
| DEQ001 | error | A non-abstract context class is not `partial`. | Nothing generated. | Add `partial`. |
| DEQ002 | error | The attributed declaration is not an ordinary class (record, struct, record struct, interface, static class), is generic, `file`-local, nested in a generic/non-partial/`file`-local type, or does not derive from `DeepEqualsContextBase`. | Nothing generated. | Declare `public partial class X : DeepEqualsContextBase` at namespace level or inside a non-generic partial type. |
| DEQ016 | error | A generated identifier (a convenience property, wrapper, core, accessor or the reserved `Cache`, `GetEqualityComparer`, `MaxComparisonPairs`, `MaxUnorderedCollisionRun`) collides with a member you declared on the context. | Nothing generated. | Rename or remove the user member; the context's user half should be empty. |
| DEQ020 | warning | An abstract class or interface, registered explicitly or reached as a member's static type, has no concrete type in the closure assignable to it. | Generation continues; null compares fine, every non-null value throws `DeepEqualsUnknownTypeException`. | Register the possible runtime types with `[GenerateDeepEquals]`. |

## Registrations

| Id | Severity | When | Effect | Fix |
|---|---|---|---|---|
| DEQ010 | error | A registered type is an open generic, `void`, a pointer or function pointer, a `ref struct` (including `Span<T>`), a static class, a delegate, a multi-dimensional array, an interface with an unimplemented `static abstract` member, or an error type. | Nothing generated. | Register a closed, ordinary type. Registering the same type twice is not a diagnostic; duplicates merge. |
| DEQ018 | error | Closure construction exceeded its bounds: a constructed type nested more than 8 generic levels deep, or more than 4,096 types. Usually expansive generic recursion such as `class Expand<T> { Expand<List<T>> Next; }`. | Nothing generated; the message names the member path. | Ignore the expanding member or register a custom comparer for the type. |
| DEQ021 | warning | A type's convenience property would be named `Equals`, `GetHashCode`, `ReferenceEquals`, `GetType`, `ToString`, `MemberwiseClone`, `Finalize`, `Instance`, `Cache` or `GetEqualityComparer`. | Only the convenience property is omitted. The wrapper, cores, dispatch cases and `GetEqualityComparer<T>()` remain. | Use `GetEqualityComparer<T>()` or the wrapper's `Instance`. |
| DEQ038 | error | A type in the closure is marked `[Obsolete]` as an error, itself or through a containing type or type argument, and the context class is not obsolete. Generated code names every closure type, and CS0619 cannot be suppressed. User code can reach such a type only from inside an obsolete type. | The context is not generated. | Mark the context class `[Obsolete]`, which makes every generated member an obsolete context; or exclude the member that reaches the type with `[DeepEqualsIgnore]`. |

## Custom comparers and simple types

| Id | Severity | When | Effect | Fix |
|---|---|---|---|---|
| DEQ006 | warning | The `[CustomEqualityComparer]` type implements zero or more than one `IEqualityComparer<T>`. | Registration ignored; `T` is compared as if unregistered. | One comparer class per `T`, implementing exactly one `IEqualityComparer<T>`. |
| DEQ007 | warning | No unambiguous way to obtain the comparer instance: no public static `Instance`/`Default` and no public parameterless constructor, an abstract/interface/struct type without a named member, or a named member that is missing, ambiguous or not assignable to `IEqualityComparer<T>`. | Registration ignored. | Add `Instance`, a parameterless constructor, or use the `(Type, "MemberName")` overload. |
| DEQ008 | warning | A custom rule overlaps a simple rule for the same or an assignable type, or two incompatible custom registrations target one type. | Custom wins over simple. For duplicate custom targets, the first in source order is kept. | Remove the redundant registration. |
| DEQ009 | warning | Two equally specific interface comparers apply to one member. | The first in source order is used. | Not currently reported; unrelated covering interface rules are `DEQ031`. |
| DEQ019 | warning | `handleNulls: true` on a comparer for a non-nullable value type. | Flag ignored; a struct has no null. | Drop the flag, or target `S?`. |
| DEQ025 | warning | Two `[SimpleType]` rules overlap by assignability, such as `Base` and `Derived`. | The narrower rule is ignored; behaviour equals registering only the broader one. | Remove the narrower registration. |
| DEQ028 | warning | `[SimpleType(typeof(S?))]` names a nullable value type. | Normalized to `[SimpleType(typeof(S))]`, which covers both `S` and `S?`. | Register `S`. |
| DEQ029 | warning | A custom comparer targets `S?` with `handleNulls: false`, so it never receives null. | Kept as registered; null is handled first and present values are passed still typed `S?`. | Register a comparer for `S` instead, or set `handleNulls: true` if the comparer defines null. |
| DEQ030 | warning | Two custom registrations overlap by reference assignability (`Base` and `Derived`, or an interface and an implementation). | The narrower registration is ignored regardless of declaration order; the broader comparer and its null policy apply to both. | Remove the narrower registration, or make the types unrelated. |
| DEQ031 | error | Two unrelated custom interface rules both cover one reached type. | Nothing generated. | Remove one rule, or register the covered type directly so the exact rule wins. |
| DEQ035 | warning | A `[CustomEqualityComparer]` whose `T` is `object`. | Registration ignored; it would make every reference type a leaf. | Register the specific types. |

## Members

| Id | Severity | When | Effect | Fix |
|---|---|---|---|---|
| DEQ003 | error | A compared member's type is not accessible from the context: a private nested type, an `internal` type in another assembly without `InternalsVisibleTo`, or a `file`-local type. | Nothing generated. There is no automatic ignore, because skipping a field silently changes equality. | Expose the type, add `InternalsVisibleTo`, or exclude the member with `[DeepEqualsIgnore]` on the member or `[DeepEqualsIgnore(typeof(Declaring), "name")]` on the context. |
| DEQ005 | warning | A member is a delegate or event. | Skipped. | Ignore explicitly to silence, or register a custom comparer for the containing type. |
| DEQ012 | error | A member's type is a `ref struct`, pointer, `ref` field, or is/contains an interface with an unimplemented `static abstract` member, none of which can be an `IEqualityComparer<T>` argument. | Nothing generated. | `[DeepEqualsIgnore]` the member, or declare it with a concrete type. |
| DEQ023 | warning | A member's type contains `dynamic` anywhere (`dynamic`, `dynamic[]`, `List<dynamic>`, a tuple item). | The whole member is ignored. | Declare the member with a static type. |
| DEQ032 | error | A member is a `fixed` buffer or its struct type carries `[InlineArray]`; a field walk would see only element 0. | Nothing generated. | `[DeepEqualsIgnore]`, a context-level ignore, or a `[CustomEqualityComparer]` for the containing type. |
| DEQ033 | warning | A context-level `[DeepEqualsIgnore(typeof(T), "name")]` matches no selected field or stored property on `T`. | The ignore has no effect. | Check the declaring type (it must be the type that declares the storage, not a derived type) and the member name. |
| DEQ034 | warning | `[DeepEqualsIgnore]` on a computed property without compiler storage, or on an uncaptured primary-constructor parameter. | Nothing to exclude; the member was never compared. | Remove the attribute. |

## Collections and shapes

| Id | Severity | When | Effect | Fix |
|---|---|---|---|---|
| DEQ004 | warning | A member declared as `IEnumerable<T>`, `IReadOnlyCollection<T>` or an unknown concrete enumerable may be lazy or non-repeatable; it will be enumerated separately for equality and hashing. Known materialized BCL types (arrays, `List<T>`, `Queue<T>`, `Stack<T>`, `LinkedList<T>`, sets, dictionaries, immutable collections, `ReadOnlyCollection<T>`) do not warn. | Compared as an ordered sequence. | Declare a materialized type, or suppress the warning if the sequence is always repeatable. |
| DEQ014 | error | A member's type is a multi-dimensional array (`T[,]`, `T[,,]`) and not a leaf. | Nothing generated. | Register a `[CustomEqualityComparer]` for the array type, use a jagged array, or ignore the member. |
| DEQ015 | warning | A collection-shaped type implements more than one instantiation of the winning family with different type arguments, such as `IReadOnlyDictionary<string, int>` and `IReadOnlyDictionary<int, string>`. | The first instantiation in the type's interface order is used and named in the message. | Register a `[CustomEqualityComparer]` for the type to compare it as a whole, ignore the member, or implement one instantiation. |
| DEQ022 | warning | `System.Text.RegularExpressions.Regex` enters the closure without a custom comparer. | Compared by reference identity. | Register an `IEqualityComparer<Regex>` with `[CustomEqualityComparer]` for pattern/options semantics. |
| DEQ027 | warning | A non-framework type is classified as a collection shape and also declares instance storage, which collection semantics ignore. | Compared as the collection; the extra fields are not compared. | Register a `[CustomEqualityComparer]` for whole-object semantics. |

## Referenced assemblies

| Id | Severity | When | Effect | Fix |
|---|---|---|---|---|
| DEQ017 | error | A class that must be walked comes from an assembly marked `ReferenceAssemblyAttribute`, whose private fields are stripped; or a struct from such an assembly exposes only `_dummy`/`_dummyPrimitive` placeholders. | Nothing generated; a zero-member comparer would report every instance equal. | For a `ProjectReference`, set `<CompileUsingReferenceAssemblies>false</CompileUsingReferenceAssemblies>` in the context project. For a package `ref/` assembly, that property has no effect: use `[SimpleType]` or `[CustomEqualityComparer]`. |
| DEQ024 | warning | A `System`/`System.*` type is neither a built-in leaf nor a recognized shape and would be walked through its visible fields, which may be reference-assembly placeholders. | Walked by visible fields. | Register `[SimpleType]` or a `[CustomEqualityComparer]`. |
| DEQ026 | warning | A struct that must be walked comes from a `ReferenceAssemblyAttribute` assembly and exposes plausible layout fields. | Its visible fields are compared, which may omit private state. | Prefer the implementation assembly, or register a leaf rule. |

## Naming

| Id | Severity | When | Effect | Fix |
|---|---|---|---|---|
| DEQ011 | error | Two types still produce the same generated identifier after every collision-resolution step: generic arity, namespace qualification, both, then a SHA-256 digest of the assembly-qualified identity. | Nothing generated. | Practically unreachable; rename one type. |

## Options and bounds

| Id | Severity | When | Effect | Fix |
|---|---|---|---|---|
| DEQ013 | warning | An option is out of range: `MaxSwitchCases` or `MaxBinaryExpressionArity` below 1, `MaxComparisonPairs` outside 1..2^29, `MaxUnorderedCollisionRun` outside 1..512, `StructPassByValueMaxByteSize` below 0, `MaxDepth` outside 1..1,000,000, `MatchingHashDepth` outside 1..16, a `CycleHandling` value the enum does not define, or a null/empty interface prefix. | The default is used for that option; an invalid prefix is ignored. | Use a value in range. |
| DEQ037 | warning | An option is set explicitly where the context's `CycleHandling` never reads it: `MatchingHashDepth` under `Tree`, where the fingerprint is the full depth-bounded hash, or `MaxDepth` under `Graph` or `Path`, which bound a comparison by `MaxComparisonPairs` instead. An option set on a base context counts. | The value is ignored and left as written, so switching the mode back needs no edit. | Remove it, or switch `CycleHandling`. |

## Package versions

| Id | Severity | When | Effect | Fix |
|---|---|---|---|---|
| DEQ036 | error | The referenced `DeepEquals.SourceGeneration.Framework` assembly's version differs from the `DeepEquals.SourceGenerator` package's. The two ship together and generated code binds the framework surface of its own version. | Nothing generated. | Reference both packages at the same version. |

## Generator failure

| Id | Severity | When | Effect | Fix |
|---|---|---|---|---|
| DEQ099 | error | The generator threw an unexpected exception while analysing or emitting this context. | Nothing generated; the message carries the exception type and message instead of an `AD0001` analyzer crash. | Report the exception with the context that triggered it. |
