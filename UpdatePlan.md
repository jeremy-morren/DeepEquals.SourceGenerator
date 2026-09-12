# Update plan: cycle handling, raw bits, bit blocks

Applied on top of the `splitFiles` branch. Revised after the audit in `Astra.md`; the findings it raised are folded
in where they apply and listed in §7.

## Status

| Step | State | Notes |
|---|---|---|
| §0 defects, header, cancellation, version policy | Done | B1 to B5 fixed; `ContextDeclarationTests` holds the regressions and the DEQ036 test. The header is computed once per context and empty type files are never opened. Cancellation is checked inside Tarjan, `Propagate`, `SelectGuards` and `HasUnguardedCycle`. Generator tests: 78 pass on net10.0. |
| 1 options plumbing | Done | `CycleHandling`, `MaxDepth` and `MatchingHashDepth` on the attribute, the options record, the reader with DEQ013 and DEQ037, three header lines, incremental and cancellation tests. Generator tests: 89 pass. Nothing is emitted differently yet. |
| 2 raw-bit shims | Done | The §2.1 shims in `DeepEqualsHelpers` and their helper theories. The rest of this step was removed in step 13. |
| 3 raw-bit emitter | Done | Float, double and Half go through the §2.1 shims; `HasSingleToInt32Bits` and the old netstandard2.0 shim are gone; the semantic-model conversion scan (now `RawBitsTests`). The DEQ036 test's override is async-local so it cannot leak into parallel tests. The rest of this step was removed in step 13. |
| 4 bit blocks | Done | `DeepEqualsBlocks` (byte compare, XxHash3 over spans, list and enumerable copies, `HasSize`), the model's bit-block classification and padding walk, the byte paths in every ordered core, `BlocksTests` and `BitBlockTests`. Deviations from §2.2, all deliberate: **System.IO.Hashing is chosen per asset**, the newest release that builds without a warning on every runtime that asset serves: 10.0.12 on net10.0, net8.0 and netstandard2.0 (.NET Framework 4.6.2 and later), 8.0.0 on net6.0 because 9.x and 10.x warn on net6.0 and net7.0; **the netstandard2.1 asset has no block helpers**, because every release warns on the netcoreapp3.1 to net5.0 runtimes it serves, so the generator probes `HasFrameworkBlocks` there, a target-framework capability, not a version one; **System.Memory 4.6.3 is pinned on netstandard2.0**, so .NET Framework consumers now gain `ReadOnlySpan<T>` and the span paths; that exposed a latent gap, fixed with a new `HasImmutableArrayAsSpan` probe, since older immutable-collections releases lack `AsSpan()`; **only source-declared structs qualify**, because Roslyn exposes no layout for metadata structs and drops `[StructLayout]` from `GetAttributes()`, so layout is read from syntax; **the flag is one `static readonly bool` per struct in the context file**, and the fallback test rewrites it in the compiled output rather than through a hook; **`HashEnumerable` hashes what it enumerates**, since the advertised count only sizes the first buffer, and the test is named for that. Generator tests 148 on net8.0/net10.0, framework 88 (86 on net472, 80 on netcoreapp3.1), fixtures 8 on all five tiers. A compiler quirk found on the way: Roslyn folds a decimal constant in `new[] { 1.50m }` converted straight to a span, losing its scale; tests go through locals. |
| 5 fingerprint levels | Done | Hash levels generalize from {1, 0} to {k .. 0} through one rule in `HashOrOmit`: an edge leaving the component calls the full hash, a structural edge keeps the level, a payload edge steps down. `HashName`/`MembersHashName` name every level; `MatchHashCode_T_L{n}` exists only for types in a component some set or dictionary entry belongs to (`TypeModel.MatchHashLevels`). Class, struct, dispatch and product cores loop over their levels; ordered containers get a plain-walk core per deeper level; unordered containers run their sum at each level; the entry fingerprint calls the level-k core. `MatchingHashDepthTests` (the audit's 65-chain case at depths 1, 2, 4, 16 and caps 64/65, rolled-versus-unrolled congruence inside sets at depths 1 to 4, emission scope, and a 70-entry set through lists, dictionaries, tuples and dispatch) lives in its own file rather than `CycleHandlingTests`. Generator tests 160 on net8.0/net10.0; fixtures 8 on all tiers. |
| 6 depth-aware framework | Done | `IDeepEqualsDepthHashOps`, `IDeepEqualsDepthElementOps`; `HashSpan(span, depth)`; `SetEquals`/`DictionaryEquals(..., depth)`, with the depth passed as an argument through fill, match, every trial and the commit, since `default(TOps)` cannot carry it; `DeepEqualsComplexityException(Type, int maxDepth)` and `(Type)` for a looping chain, with `TypeAtLimit`, `MaxDepth`, `IsCycle` and messages stating the Tree contract; `ThrowDepthExceeded`/`ThrowCycle`. Tests in `UnorderedTests` (forwarding, the bound across a set including a caller near the bound, brute-force matching over the depth overloads, exception arguments) and the depth `HashSpan` checks. Framework tests 94 (92 on net472, 86 on netcoreapp3.1). |
| 7 `Path` | Done | Every guard goes through `GuardScope`: under Path it takes `state.Mark()`, enters, and wraps the rest of the core in `try`/`finally { state.Rollback(pathMark); }`, so the pair leaves on every return. That replaces the planned `EqualsBody_T` split: one uniform shape for classes, `EqualsExact_T`, boxed adapters, tuples and every collection core, and exception-safe as well. Tail loops keep `TryEnter` per node and roll back once around the loop. The header notes that `MaxComparisonPairs` bounds ancestors under Path. **Pre-existing bug found and fixed:** the `IEnumerable<T>` core emitted its guard twice when spans exist, and the second `TryEnter` found the pair the first had entered and returned true without comparing a lazy side; a struct whose children are `IEnumerable` of itself reached it. The guard is now emitted once, ahead of the span compare, with a regression test under Graph and Path. `CycleHandlingTests` for Path: every guard rolled back, a 585-node tree fitting a pair budget of 16 under Path and not under Graph, rolled-versus-unrolled cycles, a diamond DAG, a hundred 50-node chains in a budget of 60, and strategy agreement on acyclic data; `HashLevelTests` and `FastPathTests` widened to Path; `PathFixtureContext`. Generator tests 179 on net8.0/net10.0; fixtures 8 on all tiers. |
| 8 `Tree` | Done | Under Tree the state parameter becomes `int depth` everywhere the model says `NeedsState`, through one `StateParam`/`StateArg` pair that also replaced every hard-coded `ref state`. Guards become `if (++depth > MaxDepth) ThrowDepthExceeded(typeof(T), MaxDepth)` plus a stack check, at the same guarded cores for equality and hashing. Hash cores take the depth and walk everything: no shallow or fingerprint levels, since `HasShallowHash` is false under Tree. Ops structs implement the depth interfaces, and `HashSpan` and the unordered calls pass the depth. A boxed struct cycle, whose guard sits on an adapter hashing never passes through, is guarded in its argument by `DeepEqualsHelpers.Descend`. Chain types loop in both equality and hashing with one guard and Brent's detector. **The detector runs after each node is compared**, so a finite chain that differs first returns false instead of throwing; the plan's §1.2 sketch checked before comparing. Tests: `CycleHandlingTests` (emission, the §1.1 contract including mutual recursion and both operand orders, `MaxDepth` bounds, 200,000-node chains under `MaxDepth = 8`, depth across nested sets and a node inside its own set, the whole-tree hash against Graph's one-level hash, the boxed cycle, sets and dictionaries, all three modes on acyclic data, and the sequence-guard regression under Tree); `TreeFixtureContext` at `MaxDepth = 64` on all five tiers. Also added here from §3.2 and §3.3: `RobustnessTests` for checked arithmetic in every mode, and a 300-type acyclic chain on a 256 KB thread in every mode, generated rather than written as fixture models. Generator tests 204 on net8.0/net10.0; fixtures 9 on all tiers. |
| 9 downstream | Done | New contexts: `DownstreamPathContext`, `DownstreamTreeContext`, and three matching contexts (depth 1 at the default cap, depth 1 at cap 512, depth 2). New models: `ListNode`, `Texts`, `CollidingId` (a simple type hashing to its group), `ReadOnlyListView<T>`, `Arrays`, and `PointArrays` in the record models, because array roots are not supported and `Point3[]` is reached through it. There are four new scenarios: TreeNode under Path and under Tree, `double[]` x1000, and `Point3[]` x1000. `Checks` adds the mode, fingerprint, collision-run and bit-block assertions. `MicroBench` gains `ColdStart()`, run by the Consumer's `--cold` and by the Blazor page before its checks. The smoke tests log both. `StrategyBenchmarks.cs` holds every §6 class. The new `DeepEquals.Generator.Benchmarks` project times generator scale and cancellation latency against the generator source; it is in the solution and in CI's build steps. Package versions bumped to the newest that builds without a warning: WebAssembly 10.0.12, Playwright 1.62.0, Test SDK 18.10.0. FluentAssertions 8 was left alone because of its licence change, and xunit.runner.visualstudio 4 because it targets xunit v3. Verified: the whole downstream builds against freshly packed packages, and the Consumer's checks report OK on net472, net6.0, net7.0, net8.0 and net10.0. The smoke suite, the browser run and the benchmarks are step 11. |
| 10 docs | Done | README: tier table with the hashing packages, the version policy, netstandard2.0 spans, sealing, the four options, "Choosing a cycle mode", bit blocks, the exception rows and the corrected limits. Implementation.md: the three modes, fingerprint levels, guards per mode, Brent tail loop, raw-bit leaves, bit blocks, depth overloads, `DeepEqualsBlocks`, the version policy and capabilities, the cross-context collision step, allocation rows; stale entries removed (dictionary fast path, `SingleToInt32Bits`, decimal allocation, partitioned output). Diagnostics.md was updated with steps 7 and 9. |
| 11 repack and run | Done | Run on 2026-09-12 after the go-ahead. Every suite green in Release; smoke green including Mono and the browser; benchmarks on net10.0, net8.0 and net472, the browser table and the generator scale. Results and findings in §8. The generator-scale sizes were cut from 4,000 to 2,000 classes, since 4,000 exceeds the 4,096-type closure cap and timed a `DEQ018` refusal; setup now throws on any generator error. |
| 12 `MaxDepth` without `Tree` | Done | Added by request: `DEQ037` when `MaxDepth` is set explicitly and `CycleHandling` is not `Tree`, the mirror of the `MatchingHashDepth` rule. `OptionsReader` tracks where `MaxDepth` was written, including on a base context, and the message names the mode. Tests: two `Diagnostics_are_reported` rows (Graph, Path), `An_option_that_applies_to_the_mode_does_not_warn`, and `MaxDepth_outside_Tree_is_ignored_and_the_warning_names_the_mode`. Diagnostics.md's `DEQ037` row covers both cases; the README already did. |
| 13 drop `XxHash64` | Done | By request after §8: the 64-bit stream was nowhere faster on x64 and only tied in the browser. Removed the `Hashing` option and `DeepEqualsHashing`, `DeepEqualsHashCode64`, the 64-bit ops interfaces, the 64-bit decimal and Guid readers, the emitter's word-width machinery, the 64-bit contexts, scenarios, benchmark columns and tests. `DeepEqualsBlocks` keeps XxHash3 with its own per-process seed and returns the 32-bit fold (`HashBytes`, `HashBlock`, `HashReadOnlyList`, `HashList`, `HashEnumerable`). The storage-bits scan moved to `RawBitsTests`. |
| 14 headers in the context file only | Done | By request after §8. Type files carry only the `<auto-generated>` notice and the compiler directives they need; the block comment describing the context is in the context file alone, and it no longer lists the discovered types (the closure). The 2,000-class every-root closure now generates 25.3 MB instead of 146 MB, the same as with one root. `BasicGenerationTests` asserts both shapes; the strategy tests that read closure lines check the generated code or behaviour instead. |
| 15 trimming notices in the affected files | Done | By request. The context header no longer lists the types compared through delegate field accessors; each such type's own file carries a notice below the auto-generated block saying whether it reads private fields through delegates itself or reaches a type that does, and which attributes its public comparer carries. `BasicGenerationTests.Trimming_notices_sit_in_the_files_of_the_types_they_affect` covers it on .NET 8, where a generic declaring type takes the delegate path, and checks no notice appears where `[UnsafeAccessor]` is used instead. |
| 16 equality at least as fast as a record's | Done | By request. On .NET 10 a record struct's `Equals` inlines, devirtualizes `EqualityComparer<T>.Default` to `decimal.Equals` and `string.Equals`, and reads its backing fields at their own widths. Ours read auto-properties through getters, so a `decimal` arrived as a copy whose 64-bit reads in `DecimalEquals` stalled on store forwarding, and a struct member was copied once per access. Now an auto-property of a value type wider than a register is read in place through `[UnsafeAccessor]` where available; a struct or decimal payload of a nullable is reached by reference (step 17: through `Nullable.GetValueRefOrDefaultRef`); and a stateless struct comparer's `Equals` and `GetHashCode` are `AggressiveInlining`. Probe on .NET 10, record against generated: Money 3.19 vs 1.52 ns, Invoice 10.6 vs 9.0, Person 11.5 vs 9.6, and through `IEqualityComparer<T>` likewise; .NET 8 the same or level. Point3 alone measures below the harness's resolution on both sides. Reading doubles in place too was tried and reverted: the JIT emits the same load, and it cost Point3 its use-site inlining on .NET 8. |
| 17 rely on the BCL, not framework wrappers | Done | By request: the framework ships as little as possible. Generated code calls `Nullable.GetValueRefOrDefaultRef` directly, probed on the core library; `DeepEqualsHelpers.NullableValueRef` from step 16 is gone. (Step 19 later removed the CS9192 and CS9193 suppressions this step added, by passing `in` on variables only.) Two more wrappers went the same way: `AsWritableRef` (generated code calls `Unsafe.AsRef(in x)`) and `HalfBits` (it calls `BitConverter.HalfToUInt16Bits`), and the unused `GetNullableValueRefOrDefaultRef`. The remaining helpers do work the BCL has no call for: per-asset float and double reads, decimal, Guid and DateTime storage, the constrained `EquatableEquals`, and the throw helpers. |
| 18 obsolete types and members | Done | By request. A member obsolete as an error was read through its getter and broke the build with CS0619; obsolete members are now read through their storage. The comparer class and convenience property of an obsolete type, including one built from an obsolete type argument, repeat its `[Obsolete]` (message, error flag, `DiagnosticId`, `UrlFormat`). A type obsolete as an error compiles when the context is `[Obsolete]`, and otherwise reports the new `DEQ038`. This step also wrote one pragma line per suppression with its reason; step 19 removed every suppression for the generator's own code. Tests: `ObsoleteTests`. |
| 19 generated code raises no warnings | Done | By request: avoid warnings, suppress only what cannot be avoided. The generator tests now compile every generated file at the highest warning level with documentation diagnostics and fail on any warning in it. With every generator-owned suppression removed, three warnings fired and were fixed at the source: CS1591, missing XML documentation, now written on every public member (`<summary>` on comparers, properties, `Instance` and `GetEqualityComparer<T>()`, `<inheritdoc/>` on `Equals`/`GetHashCode`); CS8981, a lower-case type name, now redeclared verbatim (`@c`); CS9192, now avoided by calling `Nullable.GetValueRefOrDefaultRef(in …)` on variables only and `GetValueOrDefault()` on values. CS0108, CS0162, CS0168, CS0219, CS8019 and CS8632 never fired; their pragmas went. `#nullable enable annotations` became full `#nullable enable`: the only warnings full analysis raised were 55 CS8620, one core serving every nullability spelling of a type, and a cast turns them into CS8619, so the private implementation is declared under `#nullable disable annotations`, where an oblivious parameter accepts every spelling and bodies keep flow analysis. A warning-level obsolete member is read through its accessor, so CS0612 and CS0618 are written only for what generated code must name. What remains, and why it cannot be avoided: an obsolete type in a generated name (the cores must name it, and marking them `[Obsolete]` moves the warning to the non-obsolete comparers that call them); an obsolete member where no accessor exists; `[Experimental]` and preview-feature ids (an experimental context silences only its own body, not its callers). A file whose types use none of these has no pragma. |
| 20 expression-bodied members | Done | By request. A method whose body is a single `return` is now written with an arrow on the signature's line: the comparer's `Equals` and `GetHashCode` (`public bool Equals(Person? x, Person? y) => Equals_Person(x, y);`), `EqualsMembers_T`, `HashMembers_T`, struct `Equals_T` and `GetHashCode_T`, and the boxed adapters. A multi-line expression starts on the line after the arrow. Whether a body is only a return depends on cycle guards and preludes that write nothing for most types, so `CodeWriter.Member` opens a block as before and, when it closes, rewrites it as an expression body if the last `Return` was all it held. A body with a prelude, a loop or a `try` keeps its block. In the net10.0 fixtures, 266 member bodies became arrows and none that is a single return is left. Tests: `A_member_whose_body_is_one_return_is_written_as_an_expression_body`. |
| 21 smaller generated files | Done | By request: reduce generated size, after measuring where it goes (§9.4). The framework classes generated code names most (`DeepEqualsHelpers`, `DeepEqualsHashCode`, `DeepEqualsState`, `DeepEqualsCollections`, `DeepEqualsUnordered`, `DeepEqualsBlocks`, `DeepEqualsReflection`) are named through `Deq*` aliases, `DeqHelpers.FloatBits(x.A)`; each file declares only those it uses, inside its namespace declaration, where an alias shadows a same-named type of an enclosing namespace and a `global using` alias, and clashes only with a type of that name in the context's own namespace. A type file's notice is the one-line `// <auto-generated/>`; the context file keeps the full notice, and each comparer carries the version in `[GeneratedCode]`. From C# 10 the namespace is file-scoped, one indentation level less on every line; below that it stays a block. Two-space indentation was offered and declined. Folding the null-check prelude into one expression was measured and rejected (§9.4). The downstream models' output shrank 20.0% and the 2,000-class closure's to 19.5 MB. Tests: `Files_name_the_framework_through_aliases_inside_the_namespace` (C# 9 and latest), `Aliases_shadow_same_named_outer_types_and_global_usings`, and the header assertions in `Output_is_one_context_file_plus_one_file_per_emitted_type`. |

Steps 0 to 4 went into one commit, because they were finished before per-step commits were asked for and share files; every step from 5 on is its own commit.

New per-context options on `[DeepEqualsSourceGenerationOptions]`:

| Option | Values | Default | What it changes |
|---|---|---|---|
| `CycleHandling` | `Graph`, `Path`, `Tree` | `Graph` | How a comparison remembers where it has been, and therefore what a cyclic type costs |
| `MaxDepth` | 1 .. 1,000,000 | 512 | `Tree` only: the guarded nesting depth past which a comparison throws |
| `MatchingHashDepth` | 1 .. 16 | 4 | `Graph` and `Path` only: how many payload edges into a cycle the fingerprint used inside unordered matching looks |

Dropped from the earlier brainstorm, on purpose: a stable hash mode and a declared-type polymorphism mode. Users seal
classes for dispatch performance; the README will say so.

All options are per context only. A per-root override would need two variants of every shared core, which is not
worth it.

**Version policy.** The generator package and the framework package ship at the same version and must be used at
the same version. The generator reads the referenced framework assembly's version once and reports one error,
`DEQ036`, when it differs from its own; nothing else in the generator reasons about older or newer framework
assets. The existing capability probes are a different axis, target framework rather than package version, and stay
as they are. Mismatched package versions after shipping are a documented trip hazard, not something the generator
works around.

---

## 0. Prerequisites: the audit's confirmed defects

Fixed first, with the audit's probe tests from `artifacts/AuditProbeTests.cs` retained as regressions. Each is
small, and B5 touches the naming table this plan extends.

| Id | Defect | Fix | Files |
|---|---|---|---|
| B1 | A context or containing type named with a keyword (`@class`) makes an invalid hint name and an invalid declaration | Hint names come from the unescaped names; emitted declarations go through `Identifier(...)` | `ContextAnalyzer.HintNamePrefixFor`, `Emitter.Open` |
| B2 | Two contexts whose names differ only by case collide on hint names across the compilation | Hint names are made unique per compilation, case-insensitively, with a short digest suffix on collision | `DeepEqualsGenerator`, `Emitter.EmitFiles` |
| B3 | A context nested in a struct or record emits `partial class` for the container | The model keeps each containing type's kind; validation reports a non-partial container with its own message | `ContextAnalyzer.ValidateDeclaration`, `ModelBuilder`, `Emitter.Open` |
| B4 | `ExcludeInterfacesByPrefix = null` throws inside the generator | Null reads as empty | `OptionsReader.ReadPrefixes` |
| B5 | `HashMembers_T` and `ShallowHashMembers_T` are not reserved, so a user member of that name gives CS0111 instead of DEQ016 | Every emitted identifier is reserved from one table, including the names this plan adds | `Naming.IdentifiersFor` |

Also in this step, because they are cheap and the plan touches the same code:

- O1, the per-type header rebuilding lists it never prints. Context-invariant header values are computed once, a
  type file gets its own line only, and a file whose body would stay empty is never opened.
- The cancellation half of O2: `Propagate`, `SelectGuards` and `HasUnguardedCycle` check the token inside their
  loops, and a test cancels during model construction of a large closure. The algorithmic half of O2 stays
  deferred behind the generator-scale benchmark in §6.

Regression tests, in `BasicGenerationTests` and `StrategyAndDiagnosticTests`:

- `Keyword_named_context_namespace_and_container_generate_and_compile`.
- `Contexts_differing_only_by_case_get_distinct_hint_names`.
- `Context_nested_in_struct_and_record_emits_the_right_container` and `Non_partial_container_reports_its_own_error`.
- `Null_empty_and_null_element_exclusion_prefixes_are_handled`, including an inherited override.
- `Every_generated_identifier_is_reserved`: a user member for each name in `IdentifiersFor` reports DEQ016.

---

## 1. `CycleHandling`

### 1.1 What each value means

| Value | Guard at a cyclic core | State | Real cycles | Shared subgraphs (DAG) | Hash |
|---|---|---|---|---|---|
| `Graph` (today) | `state.TryEnter(kind, x, y)`; a pair already seen is equal | `DeepEqualsState`, retained for the whole call | Handled, coinductive | Each pair compared once | Levels: one payload edge into a cycle, then shallow |
| `Path` | `TryEnter`, then `Rollback` to the mark when the core returns | `DeepEqualsState`, holds only the ancestors of the current pair | Handled, coinductive | Re-compared per path; can go exponential on heavy sharing | Levels, as `Graph` |
| `Tree` | `if (++depth > MaxDepth) throw`, plus the execution-stack check | One `int depth` by value | The traversal is bounded; see the contract below | Re-compared per path | Full walk, bounded by `MaxDepth`; no levels, no shallow cores |

`Tree` is the option for deserialized API models: no pair table, no pool rentals, no pair budget, and a hash that sees
the whole tree instead of one level. `Path` is the middle ground for graphs that are almost trees.

Semantics under `Graph` and `Path` are identical. `Tree` differs in two visible ways: a traversal that would not
terminate throws instead of comparing equal, and hash values differ because the whole tree takes part. Hash values
are already per process, so that is a documentation note, not a break.

**The `Tree` contract.** `Tree` bounds the traversal it performs. It does not validate that its inputs are acyclic,
and it is not promised to throw on every cyclic input:

- `Equals(a, a)` returns true from the reference-equality prelude even when `a` closes a cycle.
- Two roots that share a cyclic child return true when the comparison reaches that shared reference.
- A member that compares unequal before the traversal reaches a cycle returns false.
- A cycle that the traversal does enter throws `DeepEqualsComplexityException` once the guarded depth passes
  `MaxDepth`, or at once from a tail loop. The exception names the type whose guard hit the bound, which is where
  depth ran out, not necessarily the type that closed the cycle.

The docs and the exception message say exactly this. Users with in-memory graphs keep `Graph`.

### 1.2 Generated shape under `Tree`

State parameters become depth parameters. Everything the model computes today as `NeedsState` stays as is; only what
the emitter writes for it changes.

```csharp
// wrapper
public bool Equals(Node? x, Node? y) => Equals_Node(x, y, 0);
public int GetHashCode(Node? o) => GetHashCode_Node(o, 0);

// guarded core (was: if (!state.TryEnter(Kind_Node, x, y)) return true;)
private static bool Equals_Node(Node? x, Node? y, int depth)
{
    if (object.ReferenceEquals(x, y)) return true;
    if (x is null || y is null) return false;
    if (++depth > MaxDepth) DeepEqualsHelpers.ThrowDepthExceeded(typeof(Node), MaxDepth);
    global::System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
    return EqualsMembers_Node(x, y, depth);
}
```

`depth` is by value, so callees see the incremented copy and nothing is decremented on return or on exception. The
stack check stays in the guard because `MaxDepth` is a cycle detector, not a stack bound: a real overflow surfaces as
`InsufficientExecutionStackException`, never a crash.

**Hashing carries depth everywhere.** The hash under `Tree` takes the same parameter, has the same guard, and calls
`GetHashCode_T(o, depth)` for every edge. No `ShallowHashCode_T` or `ShallowHashMembers_T` is emitted;
`HasShallowHash` is forced false in the model. Every callback that hashes an element also takes the depth, so the
bound holds across collection boundaries and a `Node -> HashSet<Node> -> Node` cycle cannot restart its budget:

```csharp
public interface IDeepEqualsDepthHashOps<in T>    { int   GetHashCode(T x, int depth); }
public interface IDeepEqualsDepthHashOps64<in T>  { ulong GetHashCode(T x, int depth); }
public interface IDeepEqualsDepthElementOps<in T> : IDeepEqualsDepthHashOps<T> { bool Equals(T x, T y, int depth); }
```

`HashSpan`, `HashSpan64` and the unordered materialization (`FillSet`, `FillDictionary`) get overloads taking
`int depth` and forwarding it to the ops. A collection's own guard has already incremented the depth when these run,
so they receive the current value and pass it on unchanged; the element's guard increments again. The boxed adapter
and dispatch cores forward the depth like any other core.

**Tail loops.** A type with a `TailMemberIndex` (a linked list) is compared in a `while (true)` loop today, with
`TryEnter` per iteration. Under `Tree` the loop must still stop on a real cycle without growing `depth`, otherwise a
200,000-node chain would hit `MaxDepth`. Brent's cycle detection on the `x` side does it at one reference compare
per iteration:

```csharp
Node? tortoise = x; int power = 1, lambda = 1;
while (true)
{
    ... members except the tail ...
    x = x.Next; y = y.Next;
    if (object.ReferenceEquals(x, tortoise)) DeepEqualsHelpers.ThrowCycle(typeof(Node));
    if (lambda++ == power) { tortoise = x; power <<= 1; lambda = 1; }
}
```

One side is enough for termination: when either operand's chain is finite the loop ends at its null with the
ordinary null rule, and only when both are cyclic does the detector have to fire. The hash of such a type under
`Tree` also loops, since it now walks the chain: one `Streaming` (or `Streaming64`) over the non-tail members per
node, with the same Brent guard.

**Unordered collections.** A set or dictionary whose entry closure is cyclic today calls the stateful
`DeepEqualsUnordered.SetEquals` with mark and rollback per trial. Under `Tree` there is no state to roll back, so the
framework gets depth overloads over `IDeepEqualsDepthElementOps<T>`:

```csharp
public static bool SetEquals<T, TOps>(..., int maxRun, int depth) where TOps : struct, IDeepEqualsDepthElementOps<T>
public static bool DictionaryEquals<...>(..., int maxRun, int depth) ...
```

### 1.3 Generated shape under `Path`

The guarded core wraps the member chain so that the pair leaves the state when the core returns:

```csharp
private static bool Equals_Node(Node? x, Node? y, ref DeepEqualsState state)
{
    if (object.ReferenceEquals(x, y)) return true;
    if (x is null || y is null) return false;
    if (!state.TryEnter(Kind_Node, x, y)) return true;
    int mark = state.Count - 1;
    bool result = EqualsMembers_Node(x, y, ref state);
    state.Rollback(mark);
    return result;
}
```

`EqualsExact_T` and the boxed adapter get the same wrap. Guarded product and collection cores, whose bodies are
inline today, are split into the guard and an `EqualsBody_T` method so that the rollback runs on every return. Tail
loops keep `TryEnter` per iteration, since the chain is the path; `Rollback` to the mark taken before the loop runs
once after it. Sets of cyclic elements use the existing stateful ops unchanged. The state stays at depth-many pairs,
so for most data it never spills past the 8 inline slots. `MaxComparisonPairs` now bounds depth rather than total
pairs, and the header comment says so.

### 1.4 `MatchingHashDepth`: the fingerprint inside unordered matching

The audit's S1 scenario: a set of 65 chains `0 -> 0 -> i -> null` for `i` in 0..64, compared with an equal set,
throws `DeepEqualsComplexityException`. Every chain hashes the same because the hash of a cyclic type looks one
payload edge in, and the third node is the only one that differs. The data is ordinary and acyclic.

The public hash is fine as documented. The fingerprint that buckets elements inside one unordered comparison does
not have to be the public hash: it is never observed, so it can look further. `MatchingHashDepth = k` makes the
fingerprint of an element the hash that follows payload edges `k` deep into its component before going shallow,
where the public hash uses `k = 1`.

- The level machinery generalizes from levels `{1, 0}` to levels `{k .. 0}`. Level `L` follows a payload edge inside
  the component at level `L - 1`; level 0 omits it. The public `GetHashCode_T` stays level 1 and
  `ShallowHashCode_T` level 0, unchanged. Levels 2 to `k`, named `MatchHashCode_T_L{n}`, are emitted only for
  types reachable from the entry type of an unordered collection whose entry closure is cyclic.
- Coinductive congruence is kept: a level-`k` hash depends only on the `k`-bounded unrolling of the value, which is
  the same for a rolled cycle and its unrolling, so equal values under `Graph` and `Path` still fingerprint equal.
- Tail-member types fingerprint in a loop over `k` nodes.
- Cost is linear in `k` for chains and `b^k` for branching `b`, bounded by the range 1..16. The default of 4
  separates chains that differ within four nodes and keeps a 585-node tree fingerprint below its full hash.
- Under `Tree` the fingerprint is the full depth-bounded hash, so the option has no effect there. A context that sets
  `MatchingHashDepth` explicitly together with `CycleHandling = Tree` reports `DEQ037`, a warning that the option is
  ignored; the value is left as written so that switching the mode back needs no edit.

### 1.5 Files

Framework, `src/DeepEquals.SourceGeneration.Framework/`:

- `Attributes.cs`: `enum DeepEqualsCycleHandling { Graph, Path, Tree }`, the `CycleHandling`, `MaxDepth` and
  `MatchingHashDepth` properties with `DefaultMaxDepth = 512`, `MaximumMaxDepth = 1_000_000`,
  `DefaultMatchingHashDepth = 4`, `MaximumMatchingHashDepth = 16`, XML docs stating the semantics above.
- `Exceptions.cs`: `DeepEqualsComplexityException` constructors and properties for the depth bound (`Type`,
  `Depth`, `MaxDepth`) and the tail-loop cycle (`Type`), with messages that state the §1.1 contract.
- `DeepEqualsHelpers.cs`: `ThrowDepthExceeded(Type type, int maxDepth)` and `ThrowCycle(Type type)`, both
  `NoInlining`, so the guard stays one compare and one branch.
- `DeepEqualsOps.cs`: `IDeepEqualsDepthHashOps<T>`, `IDeepEqualsDepthElementOps<T>`.
- `DeepEqualsHashCode.cs`: `HashSpan` overloads taking `int depth` over the depth ops.
- `DeepEqualsUnordered.cs`: `SetEquals` and `DictionaryEquals` overloads taking `int depth` through the depth ops,
  sharing the materialization and matching code with the stateless overloads (the trial loop without mark/rollback);
  `FillSet`/`FillDictionary` overloads forwarding the depth to the fingerprint.
- `DeepEqualsState.cs`: no change. `Count`, `Mark`, `Rollback` already support `Path`.

Generator, `src/DeepEquals.SourceGenerator/`:

- `Model/ContextOptions.cs`: `CycleHandling`, `MaxDepth` and `MatchingHashDepth` fields and defaults.
- `Analysis/OptionsReader.cs`: reads all three, validates the enum against its defined values and the integers
  against their ranges, falls back with `DEQ013` like every other option; reports `DEQ037` for
  `MatchingHashDepth` set explicitly under `Tree`.
- `Analysis/ContextAnalyzer.cs`: the framework assembly version check, `DEQ036`.
- `Analysis/Naming.cs`: reserves `HashMembers_`, `ShallowHashMembers_`, `MatchHashCode_{T}_L{n}`, `EqualsBody_`,
  `MaxDepth`, `MatchingHashDepth`, and the depth ops struct names `{T}DepthOps`.
- `Analysis/ModelBuilder.cs`: `HasShallowHash` becomes `cyclic && options.CycleHandling != Tree`; the fingerprint
  level count per type; guard selection, `NeedsState`, tail members and boxed guards are unchanged; `Tree` reuses
  `NeedsState` as "needs depth".
- `Diagnostics.cs`: `DEQ036` framework version mismatch (error), `DEQ037` option without effect (warning).
- `Emit/Emitter.cs`:
  - `StateParam`/`StateArg` switch on the option: `ref DeepEqualsState state` or `int depth`.
  - `EmitCycleGuard` writes the `TryEnter` line, the `TryEnter`+mark line, or the depth line.
  - `EmitClassCores`, `EmitBoxedAdapter`, `EmitProductCores`, `EmitArrayOrListCores`, `EmitListInterfaceCores`,
    `EmitEnumerableCores`: guarded bodies under `Path` are split into wrapper and `EqualsBody_T` so that `Rollback`
    runs on every return; tail loops get the Brent guard under `Tree`.
  - `EmitUnorderedCores`: picks the depth ops interface and the depth overloads under `Tree`; the ops struct's
    fingerprint calls the level-`k` core under `Graph` and `Path`.
  - `EmitMembersHash`, `EmitDispatchHash`, `HashOrOmit`, `CaseHash`, `EntryHash`, `EmitHashOps`, `EmitSpanHash`:
    levels 0..`k` instead of 0..1; under `Tree`, level is always 1, the depth parameter is threaded through every
    hash core and ops struct, the guard is emitted, and tail-member types hash in a loop.
  - `EmitWrapper`: no state declaration, `try`/`finally` or `Dispose` under `Tree`; passes `0`.
  - `EmitConstants`: `MaxDepth` constant under `Tree`.
  - `HeaderValues`: three new "Options in effect" lines.
- `Templates/AutoGeneratedHeader.cs`: the three placeholders.
- `KnownTypes.cs`: the new framework names.
- `AnalyzerReleases.Unshipped.md`: `DEQ036`, `DEQ037`.

---

## 2. Raw bits and bit blocks

Hashing stays xxHash32 over 32-bit words. This section makes every leaf compare and hash its storage bits, and lets a
sequence of such leaves compare and hash as one block of bytes.

### 2.1 Raw bits, never a numeric conversion

Equality and hashing of a built-in leaf operate on its storage bits. No path may apply a numeric conversion to a
value: no `(uint)f` on a float, no `(long)d` on a double, no `.GetHashCode()` on a floating-point or decimal value.
A numeric conversion rounds, saturates, collapses NaN payloads and merges `-0.0` with `0.0`; the documented
representation rules keep all of those distinct, and the hash must agree with equality bit for bit.

Two qualifications. `decimal.GetBits` is a representation API, not a conversion; generated code avoids it because
the array form allocates, and tests use it as an oracle. A `[SimpleType]` or a custom comparer deliberately selects
the user's or the runtime's semantics for that type, `GetHashCode` included, and the rule does not apply to it.

The only casts allowed in a hash word are the ones C# defines as bit-preserving on integers:

| Cast | Effect | Where |
|---|---|---|
| `int` to `uint`, `long` to `ulong` (and back), always inside `unchecked(...)` | Reinterpretation, same width | the stream |
| `(int)ulong`, `(int)(ulong >> 32)` | The low and high words | the two words of a 64-bit leaf |
| `byte`, `sbyte`, `short`, `ushort`, `char` to `int` | Extension to one narrow word | narrow leaves |
| an enum to exactly its underlying type | Identity on the bits; the compiler emits nothing | enum leaves |
| `bool` as `? 1 : 0` | Not a cast; both outcomes are distinct words | `bool` leaves |

`unchecked` is written explicitly because a consumer project may compile with
`CheckForOverflowUnderflow`. `nint`/`nuint` widen to 64 bits by sign or zero extension, which preserves every
bit of the native word.

**Reinterpretation per target, without `unsafe`.** The framework project does not set `AllowUnsafeBlocks` and this
plan does not add it. The raw pointer form `*(uint*)&f` is also the slowest option: taking the address spills the
local to the stack for the rest of the method. The fastest form on each asset is:

| Value | net8.0, net10.0 | net6.0 | netstandard2.1 | netstandard2.0 |
|---|---|---|---|---|
| `float` | `BitConverter.SingleToUInt32Bits` | `BitConverter.SingleToUInt32Bits` | `BitConverter.SingleToInt32Bits` | `Unsafe.As<float, uint>(ref)` |
| `double` | `BitConverter.DoubleToUInt64Bits` | `BitConverter.DoubleToUInt64Bits` | `BitConverter.DoubleToInt64Bits` | `Unsafe.As<double, ulong>(ref)` |
| `Half` | `BitConverter.HalfToUInt16Bits` | `BitConverter.HalfToUInt16Bits` | not a leaf on this asset | not a leaf on this asset |
| `decimal`, `Guid`, `Int128`, `UInt128` | `Unsafe.As<T, int>(ref)` and `Unsafe.Add`, one 32-bit word at a time | same | same | same |
| `DateTime` | `Unsafe.As<DateTime, ulong>(ref)` (existing `DateTimeBits`) | same | same | same |
| `DateTimeOffset` | `.Ticks` and `.Offset.Ticks`, two `long` words | same | same | same |
| `TimeSpan` | `.Ticks` | same | same | same |

The `BitConverter` methods are JIT intrinsics on every CoreCLR from .NET Core 3.0 on and on Mono, so each is one
register move. `Unsafe.As` over a `ref` is intrinsic too and the JIT does not spill the local for it; it is the
only option on netstandard2.0, where `SingleToInt32Bits` does not exist, and the only option for 16-byte types
on every asset, because `BitConverter` has no overload for them. `Unsafe.BitCast` would be equivalent on net8.0 and
later but adds nothing over `BitConverter` for 4- and 8-byte values, and it is not in the out-of-band `Unsafe`
package, so netstandard stays on `Unsafe.As`. `DateTimeOffset` is read through its two tick properties rather than
reinterpreted, because its 16 bytes include padding whose content is not defined.

Every reinterpretation that differs per asset goes through one `AggressiveInlining` shim in `DeepEqualsHelpers`,
selected per asset with `#if`, so generated code binds one stable name and the emitter stops probing for
`SingleToInt32Bits`. `Half` exists only where `BitConverter.HalfToUInt16Bits` does, so generated code calls that
directly (step 17):

```csharp
public static uint  FloatBits(float value);                 // the four bytes
public static ulong DoubleBits(double value);               // the eight bytes
public static int   DecimalWord(in decimal value, int i);   // storage word i
public static int   GuidWord(in Guid value, int i);
public static ulong DateTimeBits(DateTime value);           // exists today
```

The decimal words are in storage order, which is not the `lo, mid, hi, flags` order `decimal.GetBits` returns.
Tests compare the set of words, or map explicitly, never the sequence.

The existing `SingleToInt32Bits` shim and the `HasSingleToInt32Bits` capability go away once every emitter path uses
`FloatBits`; under the version policy nothing compiled against the old shim can meet the new asset.

**Packages.** None to add for this section. `System.Runtime.CompilerServices.Unsafe` 6.1.2 is already referenced on
netstandard2.0 and netstandard2.1 and provides `Unsafe.As`, `Unsafe.Add` and `Unsafe.AsRef`; net6.0 and later have
them in the box. `System.Buffers` on netstandard2.0 is unchanged.

**Tests for this rule.** `HelpersTests` gains, on every framework the tests run on: every shim returns the bytes
`BitConverter.GetBytes` returns for the same value, for a normal value, `-0.0` against `0.0`, two NaNs with different
payloads, and `float.MaxValue`; the four decimal words together cover each `decimal.GetBits` word once; the Guid
words together equal `Guid.ToByteArray`. A generator test parses the emitted source into the test compilation and walks
its semantic model for any `IConversionOperation` from a floating-point or decimal type to an integer type, and any
`GetHashCode` invocation on such a type outside `[SimpleType]` and custom-comparer paths; every hit fails. A textual
cast scan is not enough, because casts appear in comments and in the unrelated `unchecked` integer forms.

### 2.2 Bulk paths: bit blocks, vectorized compare, XxHash3

The raw-bits rule makes a sequence of bit-hashed leaves a contiguous block of bytes. Three changes follow, in
order of value.

**Bit-block leaves.** A leaf whose equality is its storage bits and whose storage is fixed-size and reference-free:
`byte`, `sbyte`, `short`, `ushort`, `int`, `uint`, `long`, `ulong`, `char`, `float`, `double`, `Half`, `decimal`,
`Guid`, `DateTime`, `TimeSpan`, `Int128`, `UInt128`, and every enum. Not `bool` (the runtime does not normalize
its byte), not `nint`/`nuint` (size differs per process, which is fine for equality but the hash of a 32-bit
process would differ from a 64-bit one only in this one place; keep them on the word path), not `DateTimeOffset`
(padding), not `string`, `Uri` or anything with a custom or default comparer.

**Bit-block structs (item 4).** A user struct joins the bit-block set when the generator can prove it has no
padding: every field is a bit-block leaf or a bit-block struct, the layout is not `Auto` or `Explicit`, no
`Pack` or `Size` is set, the struct is not generic, and laying the fields out in declaration order at their natural
alignment leaves no gap and a total that is a multiple of the largest alignment. `Point3` (three doubles, 24 bytes)
qualifies; an `int` followed by a `long` does not. Because the runtime owns layout, the generated code also checks
at first use, once per type, that `Unsafe.SizeOf<T>()` equals the computed size, and otherwise takes the word path
for that type. That check is a `static readonly bool` the JIT folds to a constant. A single bit-block struct value
keeps its member-wise equality and word hash; the byte paths pay off on sequences of them.

**Equality of bit-block sequences (item 2).** `Equals_SpanOf<T>` for a bit-block `T` becomes

```csharp
return global::System.MemoryExtensions.SequenceEqual(
    global::System.Runtime.InteropServices.MemoryMarshal.AsBytes(xs),
    global::System.Runtime.InteropServices.MemoryMarshal.AsBytes(ys));
```

on every asset with `MemoryMarshal.AsBytes`, which `HasMemoryMarshal` already probes. This is the BCL's vectorized
memcmp and exactly the bitwise relation: NaNs with equal payloads are equal, `-0.0` and `0.0` are not. Today only
leaves whose default equality already matches ours reach `SequenceEqual`, so `float`, `double`, `decimal`, `Guid`,
`DateTime` and 64-bit enums fall to the element loop. Every span-capturing container gains this: arrays, lists,
`ImmutableArray<T>`, `ArraySegment<T>`, `Memory<T>`, and interface-typed members that capture a span at runtime.

**Hashing of bit-block sequences (item 1).** The hash of a bit-block sequence is defined as XxHash3 over its bytes
with a per-process seed, through `System.IO.Hashing.XxHash3.HashToUInt64(ReadOnlySpan<byte>, long seed)`, folded to
32 bits. The definition has to hold for every container shape of the same
declared type, because a `double[]` and a custom `IReadOnlyList<double>` holding the same values are equal and must
hash equal. So:

- Span-capturing containers hash `MemoryMarshal.AsBytes(span)` directly.
- The indexer and enumerator fallbacks for a bit-block element copy the elements into a rented `T[]` from
  `DeepEqualsPools<T>.Shared` (stack-allocated below 256 bytes on assets with `stackalloc` spans), hash the bytes,
  and return the array. A count lie on the enumerator path is handled as the unordered path handles it today.
- The stream is the same for every length, so there is no threshold to keep consistent. XxHash3's short-input paths
  are one or two multiplies for up to 16 bytes and a handful for up to 128.

`HashSpan`, `Streaming` and the `Combine` overloads remain the definition for sequences of every other element
type, and the invariant that the three agree is unchanged for those.

**Package.** `System.IO.Hashing`, latest stable, referenced on every framework asset. It targets netstandard2.0,
has no further dependencies, and is trim and AOT safe. Its `XxHash3` core is vectorized with AVX2 and NEON on
CoreCLR. On WebAssembly its 64x64 to 128-bit multiply is emulated, and whether the interpreter takes its vector path
is a measurement, not a promise; the browser table in the smoke output will show it.

**Not done.** Vectorizing the xxHash32 lanes themselves: the four scalar lanes already overlap in the CPU, and the
32-bit vector multiply is slow on Intel. Strings stay on Marvin and the runtime's own hash.

### 2.3 Files

Framework:

- `DeepEqualsHelpers.cs`: the shims of §2.1: `FloatBits` and `DoubleBits`, each `#if`-selected per asset;
  `SingleToInt32Bits` removed.
- New `DeepEqualsBlocks.cs`: `HashBytes(ReadOnlySpan<byte>)` (XxHash3 with its own per-process seed, folded to 32
  bits and never 0), `HashBlock<T>(ReadOnlySpan<T>)` over `MemoryMarshal.AsBytes`, `HashReadOnlyList<T>`,
  `HashList<T>` and `HashEnumerable<T>` with the rented-buffer copy, `BlockEquals<T>(ReadOnlySpan<T>,
  ReadOnlySpan<T>)` and `HasSize<T>`. All constrained to `unmanaged`.
- `DeepEquals.SourceGeneration.Framework.csproj`: adds `System.IO.Hashing` on every target. No `AllowUnsafeBlocks`.

Generator:

- `Emit/Emitter.cs`:
  - `LeafHash`, `LeafEq`, `SingleBits`: every float and double read goes through the §2.1 shims; `SingleBits`
    becomes `FloatBits` and the `HasSingleToInt32Bits` branch is deleted.
  - `EmitSpanCores`, `EmitSpanCompare`, `EmitSpanHash`, `EmitArrayOrListCores`, `EmitListInterfaceCores`,
    `EmitEnumerableCores`: a bit-block element selects the `DeepEqualsBlocks` calls in place of `SequenceEqual`, the
    element loop, `HashSpan` and `Streaming`. `EmitStructCores` emits the `Unsafe.SizeOf` check for a bit-block
    struct; `EmitConstants` the per-type `static readonly bool`.
- `Analysis/CapabilityProbe.cs`, `Model/TargetCapabilities.cs`: `HasSingleToInt32Bits` removed; the header's
  capability list drops that line.
- `Analysis/ModelBuilder.cs`, `Model/TypeModel.cs`: `IsBitBlock` on a type model, with `BitBlockSize`. Leaves are
  classified from the §2.2 list; structs by the padding proof, which walks the fields in declaration order with
  natural alignment and reads `StructLayoutAttribute` off the symbol.
- `KnownTypes.cs`: `GlobalBlocks`.

---

## 3. Tests

Hash tests that assert two values hash differently use fixed seeds and chosen vectors, because a legitimate
collision under a random seed would fail the run. Tests that assert equal values hash equal run over many seeds.

### 3.1 Framework tests, `tests/DeepEquals.SourceGeneration.Framework.Tests/`

`HelpersTests.cs`, added:

- `Bit_shims_return_the_storage_bytes` (theory per shim): `FloatBits`, `DoubleBits`, `DecimalWord`, `GuidWord` and
  `DateTimeBits` against `BitConverter.GetBytes`, `decimal.GetBits` as a set of words, and `Guid.ToByteArray`, for a normal value, `float.MaxValue`, `-0.0` against `0.0`, and two
  NaNs with different payloads. Runs on all five test frameworks, so netstandard2.0's `Unsafe.As` path is covered
  on net472.
- `ThrowDepthExceeded_and_ThrowCycle_carry_their_arguments`.

`UnorderedTests.cs`, added:

- `Depth_sets_of_cyclic_elements_compare_equal`: the depth overloads over the same data as
  `Stateful_sets_of_cyclic_elements_compare_equal_and_trials_roll_back`.
- `Depth_overloads_forward_the_caller_depth_to_the_fingerprint`: a counting ops struct sees the depth passed in.
- `Depth_overloads_throw_past_the_bound`.
- `Exact_matching_agrees_with_brute_force_on_every_matrix_and_permutation`: extended to the depth overloads.

New `BlocksTests.cs`:

- `HashBytes_is_seeded_XxHash3`: agrees with `XxHash3.HashToUInt64`, folded, for the same seed on lengths 0, 1, 3, 16, 17,
  128, 129, 240, 241 and 4096, and changes with the seed.
- `HashBlock_HashList_and_HashEnumerable_agree` for `double`, `Guid` and `decimal` elements, on every length above,
  with a list that is not an array and an enumerable that is not a list.
- `HashEnumerable_detects_a_count_lie`.
- `HashEnumerable_returns_its_rented_buffer` through `FakeArrayPool`, on the success and the throwing path.
- `BlockEquals_is_bitwise`: NaN payloads, negative zero, decimal scale, `DateTime` kind.
- `Empty_block_hashes_nonzero_and_null_container_hashes_zero`.

`StateTests.cs`, added:

- `Enter_then_rollback_per_level_never_spills_for_a_deep_chain_of_ancestors_under_eight`: the `Path` pattern stays
  inline. The existing rollback tests already cover the spilled case.

### 3.2 Generator tests, `tests/DeepEquals.SourceGenerator.Tests/`

New `CycleHandlingTests.cs`:

- `Tree_emits_depth_parameters_and_no_state`: source assertions, no `DeepEqualsState`, no `try`, `int depth`,
  `MaxDepth` constant, no `ShallowHashCode_`, depth on every hash ops struct.
- `Tree_contract` (theory): self-comparison of a cyclic value is true; two roots sharing a cyclic child are true;
  an unequal member before the cycle is false; a cyclic value against a finite one, in both operand orders, is
  false; a cycle the traversal enters throws with the guarding type named; a cycle through two mutually recursive
  types behaves the same; the same data under `Graph` and `Path` is equal. Equality and hashing both.
- `Tree_max_depth_is_honoured`: a branching model, `TreeNode` with children, of depth 5 with `MaxDepth = 3` throws
  and of depth 3 passes; `MaxDepth = 0` and an undefined enum value each report `DEQ013` and use the default.
- `Tree_linked_lists_loop_and_detect_cycles_with_brent`: 200,000-node chains equal and unequal without depth
  growth; a rolled list throws; a rolled list against a finite one is false in both orders; the hash of the chain
  completes.
- `Tree_depth_is_carried_across_collections`: a `Node -> HashSet<Node> -> Node` cycle throws rather than restarting
  its budget, and an acyclic object nested inside several collections at a depth just over `MaxDepth` throws while
  one just under passes. Equality and hashing both.
- `Tree_hash_walks_the_whole_tree`: two trees that differ three levels down hash differently under `Tree` (fixed
  seed) and identically under `Graph`.
- `Tree_sets_and_dictionaries_of_cyclic_typed_elements_use_the_depth_overloads`: source and behaviour.
- `Tree_boxed_struct_cycle_through_object_throws`: the `Tree` counterpart of
  `Boxed_struct_cycle_through_object_terminates`.
- `Path_terminates_on_cycles_and_unrolled_cycles_are_equal`: the fixture cycle test under `Path`.
- `Path_rolls_back_on_every_return`: source assertion that each guarded core has one `Rollback`; behaviour on a
  tree where a subtree compares unequal early.
- `Path_shared_subgraphs_are_recompared_and_still_correct`: a diamond DAG.
- `Path_tail_loops_rollback_after_the_loop`.
- `Every_strategy_agrees_on_acyclic_data`: theory over the three values against one acyclic model set, equal and
  unequal pairs.
- `Matching_hash_depth_separates_chains_that_the_public_hash_does_not` (S1): 65 chains `0 -> 0 -> i -> null` in
  two equal sets; with `MatchingHashDepth = 1` and cap 64 the comparison throws, with cap 65 it succeeds, with
  depth 2 and cap 64 it succeeds; the public hashes of all 65 stay equal; the sets compare equal under `Tree`
  regardless.
- `Matching_hash_depth_keeps_coinductive_congruence`: a rolled cycle and its unrolling fingerprint equal at every
  depth 1..4, inside a set comparison.
- `Matching_hash_depth_under_Tree_reports_DEQ037_and_is_ignored`.
- `Framework_version_mismatch_reports_DEQ036`: the test host references a framework assembly stamped with a
  different version.

New `RawBitsTests.cs`:

- `Generated_cores_contain_no_numeric_conversion`: the semantic-model walk of §2.1 over a
  closure with every floating-point and decimal leaf, including `Half`, float aggregates and nullable forms, and a
  `[SimpleType]` double wrapper that is exempt.

New `BitBlockTests.cs`:

- `Bit_block_leaves_are_classified` (theory): each type in the §2.2 list is a bit block, and `bool`, `nint`,
  `DateTimeOffset`, `string`, a `[SimpleType]` and a custom-comparer type are not.
- `Structs_without_padding_are_bit_blocks_and_padded_ones_are_not`: three doubles, `(int, int)` pair, a struct of a
  `Guid` and a `long`, a nested bit-block struct; against `int` then `long`, `byte` then `int`, a struct with a
  string, an `Auto` layout, an `Explicit` layout, a `Pack = 1`, and a generic struct.
- `Bit_block_spans_compare_as_bytes_and_hash_through_XxHash3`: source assertions on the `DeepEqualsBlocks` calls
  for `double[]`, `List<Guid>`, `ImmutableArray<decimal>`, `IReadOnlyList<float>`, `IEnumerable<Point3>`, and no
  such call for `string[]`, `bool[]` or `List<int?>`.
- `Every_container_shape_of_a_bit_block_sequence_hashes_equal`: array, list, immutable array, a custom
  `IReadOnlyList<T>`, a lazy `IEnumerable<T>`, for `double` and a bit-block struct.
- `Bit_block_sequence_equality_is_bitwise`: NaN payloads and negative zero inside arrays.
- `Size_check_failure_falls_back_to_the_word_path`: a fixture whose computed size is made wrong through a test hook
  on the generated `static readonly` still compares and hashes correctly.

Changed:

- `HashLevelTests.Nested_lists_tuples_and_dictionaries_in_a_cycle_terminate_and_hash_consistently`: theory over
  `{Graph, Path} x MatchingHashDepth {1, 4}`.
- `FastPathTests.One_guard_per_cycle_still_terminates_and_equates_rolled_and_unrolled_graphs`: theory over `Graph`
  and `Path`.
- `StrategyAndDiagnosticTests.Diagnostics_are_reported`: `DEQ013` cases for the three new options, `DEQ036`,
  `DEQ037`.
- `BasicGenerationTests.Output_is_one_context_file_plus_one_file_per_emitted_type`: header shows the three lines and
  the type file's header no longer lists the closure.
- `IncrementalTests`: `Changing_an_option_reruns_the_output` for each new option.
- New `Generated_code_compiles_and_runs_under_CheckForOverflowUnderflow`: theory over every mode, the
  consumer compilation set to checked arithmetic, over the collection, dispatch, aliasing and boxed-cycle fixtures.
- Every test asserting a stack check follows the README wording fixed in §3.5.

### 3.3 Fixture tests, `tests/DeepEquals.Fixtures.Tests/`

`Models.cs` gains `PathFixtureContext` and `TreeFixtureContext` (registering the acyclic-data models and `Node`).
`FixtureTests.cs`:

- `Cycles_terminate_and_unrolled_cycles_are_equal` runs under `Graph` and `Path`.
- `Tree_context_bounds_its_traversal`: the §1.1 contract on the fixture `Node`.
- `Long_chains_use_one_stack_frame` runs under all three.
- `Deep_acyclic_declaration_chain_in_a_small_stack_thread`: a thread with a 256 KB stack compares and hashes a
  closure whose declaration chain is long, and gets `InsufficientExecutionStackException` or a result, never a
  crash. This is the audit's stack-safety scenario.

### 3.4 Downstream, `downstream/`

- `models/Contexts.cs`: `DownstreamPathContext` and `DownstreamTreeContext` registering `TreeNode`. `GraphNode`
  stays out of the tree context.
- `models/Scenarios.cs`: the new scenarios of §6.
- `models/Checks.cs`: the tree context follows the §1.1 contract on the graph fixture; the path context equates rolled and unrolled graphs; the S1 sets compare
  equal at the default depth.
- `models/MicroBench.cs`: `ColdStart()`, the first call per context.
- `benchmarks/DeepEquals.Benchmarks/StrategyBenchmarks.cs`: the classes of §6.
- `smoke/consumers/BlazorWasm/Result.razor`, `Consumer/Program.cs`: the smoke tests assert the new checks pass and
  log the browser table.

### 3.5 Docs

- `README.md`: the options table gains three rows, a paragraph per `CycleHandling` value with the `Tree` contract,
  the sealing note, the version policy. Stale claims fixed: the stack check is made at the `Equals` entry of a
  stateful comparer and at every cycle guard, not at every entry point; exact `decimal` allocates nothing on any
  tier; a spilled state near the default budget holds about 36 MB, the pairs, the index and the cached hashes.
- `docs/Implementation.md`: §3 (fingerprint levels), §6.1 and §6.7 (tail
  loops under `Tree`), §6.8 and §6.9 (depth overloads, fingerprint depth), §7 (the three modes, what `Path` rolls
  back, the `Tree` contract), §8 (`DeepEqualsBlocks`), §11 (the version policy), §12
  (the per-compilation hint-name step from B2), §13 (decimal row removed, hash-array row added), §14 (the
  dictionary fast path, partitioned output and vectorized combine entries corrected; the dropped modes listed).
  §6.9 and every other section are re-read for statements the current code already contradicts, since the audit
  found the docs describing work as pending that is done.
- `docs/Diagnostics.md`: `DEQ013` lists the new option names; `DEQ036` and `DEQ037` documented.

---

## 4. Order of work

0. §0: B1 to B5 with regressions, O1, and the version policy with `DEQ036`. Every existing suite stays green.
1. Options, model and naming plumbing for all three options: attribute, `ContextOptions`, `OptionsReader`,
   `Naming`, header, `DEQ013`/`DEQ037` cases, incremental test. Nothing is emitted differently yet.
2. Framework raw-bit shims: the §2.1 shims in `DeepEqualsHelpers`, framework tests.
3. Emitter: every float and double read through the shims, the storage-bits scan, the changed theories.
4. Bit blocks: the `System.IO.Hashing` reference, `DeepEqualsBlocks`, `BlocksTests`; then the model classification
   and padding proof, the emitter's span, list and enumerable paths, `BitBlockTests`.
5. Fingerprint levels (`MatchingHashDepth`) in the model and emitter, the S1 tests. Independent of `Tree`.
6. Depth-aware hash interfaces and the `Tree` exception contract in the framework, then the depth overloads in
   `DeepEqualsUnordered`, `UnorderedTests` additions.
7. `Path` in the emitter, `CycleHandlingTests` for `Path`, fixture context.
8. `Tree` in the emitter: depth threading through every core and ops struct, tail-loop Brent guard, full hash
   walk, depth ops; the `Tree` tests.
9. Downstream contexts, scenarios, checks, benchmark classes, consumer output, smoke assertions.
10. Docs, including the stale-claim fixes.
11. Repack and run: generator tests, framework tests on five frameworks, fixtures, smoke, then the benchmarks on
    net472, net8.0 and net10.0 plus the browser table. Not before you say so.

Rough effort: half a day for §0, half a day for the raw-bit shims, half a day for bit blocks, half a day for the fingerprint
levels, one and a half for cycle handling, half a day for downstream and docs.

---

## 5. Risks and decisions taken

- **`Tree` bounds its traversal and does not validate inputs.** Stated in §1.1, the docs and the exception message.
- **`MaxDepth` default 512.** System.Text.Json stops reading at 64. Linked lists do not count against it because the
  tail loop keeps depth flat. The stack check stays in the guard, so a smaller thread stack degrades to an
  exception rather than a crash.
- **`Path` on heavily shared DAGs can be exponential.** Documented; `MaxComparisonPairs` no longer caps total work
  there, only depth. Users with such data keep `Graph`.
- **`MatchingHashDepth` trades fingerprint cost for fewer collision runs.** Default 4; users with wide cyclic
  elements in large sets can lower it, users with S1-shaped data can raise it. Under `Tree` it is ignored with a
  warning.
- **Hash values change with the mode.** Already true across processes; the docs state it once more.
- **Same-version packages.** A mismatch is one error and otherwise unsupported. No per-feature fallback exists.
- **Matching keys are unchanged** inside `DeepEqualsUnordered`: its packed `(hash, index)` keys and collision-run
  logic stay as they are; only the fingerprint fed to them changes (§1.4).
- **Bit-block struct layout is the runtime's call.** The generator's padding proof is checked once per type at run
  time against `Unsafe.SizeOf<T>()`, and a mismatch takes the word path, so a layout surprise costs speed, never
  correctness. `Explicit`, `Auto`, packed and generic structs are excluded outright.
- **XxHash3 on the WebAssembly interpreter** may run its scalar fallback and emulates the 128-bit multiply. The
  bit-block scenarios in the browser table decide whether the byte path stays on for that target or the emitter
  keeps the word path there; both are one condition in `DeepEqualsBlocks`.
- **A new dependency.** `System.IO.Hashing` is the first package the framework needs on every asset. It is
  Microsoft-owned, netstandard2.0, dependency-free, trim and AOT safe.

---

## 6. Benchmarks to add for the implementation

All in `downstream/benchmarks/DeepEquals.Benchmarks`, over `models/Scenarios.cs` so the smoke checks and the
browser table run them too, unless noted. Each names what it is meant to expose.

**Cycle handling**

- `TreeNode` at branching 8 depth 3 (585 nodes, today's) and branching 2 depth 9 (1,023 nodes), under `Graph`,
  `Path` and `Tree`, equals and hash. Exposes the guard cost against the depth of nesting rather than node count.
- Linked list of 10, 1,000 and 100,000 nodes, equals and hash, under all three. Exposes the tail loop, Brent's
  guard and the `Path` state growing with the chain.
- Diamond DAG: a root whose two children share one subtree of 256 nodes, under all three. Exposes the `Path`
  and `Tree` re-comparison against the `Graph` memo; a variant with 8 levels of sharing shows the exponential case.
- Retained pairs at 8 and 9, and a `Path` comparison whose rollback follows a spill and a growth. Exposes the
  inline-to-spill boundary and the rollback cost.
- `HashSet<Node>` of 100 cyclic-typed elements, equal sets, under `Graph` (stateful trials with mark and rollback)
  and `Tree` (depth ops). Exposes the cost of trial rollback against plain recursion.

**Fingerprints and unordered matching**

- The S1 sets: 65 chains `0 -> 0 -> i -> null`, at `MatchingHashDepth` 1, 2 and 4, with the collision cap at 64,
  65 and 512. Exposes the fingerprint cost against the matching cost it removes; depth 1 at cap 64 is the throwing
  case and is asserted, not timed.
- Sets of 100 acyclic objects with 0, 10 and 100 elements sharing a fingerprint, with an early match, a late
  mismatch and expensive elements. Exposes the k-by-k matching matrix the audit's O3 targets; record the
  deep-comparison count and the pool rentals as well as time.
- Sets of 63, 64 and 65 equal-fingerprint elements. Exposes behaviour at the cap.

**Hashing**

- `HashSet<Customer>` and `Dictionary<Order, int>` built with the generated comparer: 1,000 adds then 1,000 lookups,
  against the same with the built-in comparer. Exposes hash quality and cost together, which raw `GetHashCode`
  throughput does not.
- A record with 8 strings of 8, 64 and 1,024 characters, equals and hash. Exposes how much Marvin dominates, so
  gains elsewhere are read against it.
- `Guid[]` and `decimal[]` of 1,000, equals and hash. Exposes the 128-bit leaves and the bit-block paths.

**Bit blocks**

- `double[]` and `Point3[]` at 16, 256 and 4,096 elements, equal and with a mismatch in the last element, against an
  element loop. Exposes `SequenceEqual` over bytes and XxHash3 against the word stream at each length.
- The same sequences behind a custom `IReadOnlyList<T>` that is not an array. Exposes the copy path.
- The same in the browser table. Exposes whether the interpreter takes the vector paths.

**Mismatch position**

- `Customer` and `Order` with the first member unequal and with the last member unequal. Exposes early-exit cost
  and the predicate chain order.

**Cold start**, in the Consumer rather than BenchmarkDotNet

- First call per context, timed once with the stopwatch: holder initialization, dispatch maps, the bit-block size
  checks, the `System.IO.Hashing` type load. Exposes what the new static state adds to startup, which matters in
  the browser.

**Generator scale**, a new project `downstream/benchmarks/DeepEquals.Generator.Benchmarks` with a project reference
to the generator

- Run the generator over synthetic closures of 100, 1,000 and 2,000 declared types, with one root and with every
  type a root; report elapsed time, allocated bytes and generated bytes. 2,000 classes make about 3,700 closure
  types; 4,000 would pass the 4,096-type cap. Exposes O1 and O2, and gives the baseline for the
  header and guard-selection rework.
- Cancellation latency: cancel during model construction of the 2,000-type closure and measure the time to
  return. Exposes the missing cancellation checks inside `Propagate` and `SelectGuards`.

---

## 7. Audit findings and where they landed

| Finding | Disposition |
|---|---|
| B1 to B5 | §0, fixed first with regressions |
| S1 | §1.4, `MatchingHashDepth`; tests in §3.2; benchmarks in §6 |
| P1 depth in hash callbacks | §1.2, depth-aware hash ops and overloads; tests in §3.1 and §3.2 |
| P2 compatibility | Version policy at the top; per-feature fallbacks removed |
| P3 `Tree` contract | §1.1, contract stated; §3.2 `Tree_contract`; depth test uses a branching model |
| P4 fold to zero | Not applicable: the 32-bit stream has no fold |
| P5 representation tests and packing | §2.1 wording, decimal word order, `[SimpleType]` exemption, test rename, fixed seeds, semantic scan |
| O1 header cost | §0 |
| O2 graph analysis passes | Cancellation checks in §0; the worklist rework deferred and measured by the generator-scale benchmark in §6 |
| O3 stateless matching matrix | Deferred; measured by the fingerprint benchmarks in §6 |
| O4 bulk span equality | §2.2 item 2 |
| Stack-safety boundary | README wording fixed in §3.5; small-stack fixture test in §3.3; `Tree` hashing carries the check in its guard |
| Benchmark shapes | §6 |
| Stale documentation | §3.5 |
| Plan sequencing | §4 |

---

## 8. Step 11 results

Run on one Windows x64 machine, BenchmarkDotNet `ShortRun` in process, so differences under about 10% are noise.
Raw reports are under `D:\Temp\deepequals\step11`.

**Suites.** Generator 211 on net8.0 and net10.0; framework 94, 92 on net472, 86 on netcoreapp3.1; fixtures 9 on all
five tiers; smoke 20 of 20, with Mono passing once `C:\Program Files\Mono\bin` is on `PATH` (it skips otherwise).
The browser test first failed because the local Chromium headless shell was a damaged download
(`STATUS_INVALID_IMAGE_FORMAT`); `playwright.ps1 install --force chromium-headless-shell` fixed it.

**Hash width.** XxHash64 (§9.1 describes what was built) is not faster on x64 CoreCLR. Public `GetHashCode`, XxHash32
against XxHash64, scenario harness:

| Scenario | net10.0 | net8.0 | net472 | browser |
|---|---|---|---|---|
| Customer | 23.9 / 26.4 ns | 28.0 / 30.9 ns | 44.4 / 40.2 ns | 243 / 245 ns |
| Order | 373 / 527 ns | 448 / 568 ns | 1,363 / 1,187 ns | 6,165 / 5,891 ns |
| `IReadOnlyList<OrderLine>` x100 | 1,335 / 1,246 ns | 1,537 / 1,517 ns | 3,180 / 2,722 ns | 14,680 / 14,535 ns |
| `Dictionary<string, decimal>` x100 | 733 / 1,716 ns | 837 / 1,948 ns | 3,117 / 3,067 ns | 16,509 / 12,721 ns |
| `record struct Money` | 12.9 / 54.2 ns (noisy) | 5.8 / 19.0 ns | 19.3 / 27.8 ns | 142 / 122 ns |
| `record struct Point3` | 3.2 / 6.9 ns | 4.8 / 7.1 ns | 5.7 / 7.4 ns | 94 / 109 ns |
| `double[]` x1000 (bit block) | 174 / 162 ns | 298 / 292 ns | 1,148 / 1,125 ns | 2,747 / 2,753 ns |

Why, from the code and a focused probe:

- The premise was "half the words, half the rounds". An xxHash64 tail round costs three multiplies where xxHash32's
  costs two, a 64-bit multiply is no cheaper on x64, and packing pushes most objects under four words, where both
  hashes run serial tail rounds instead of the four parallel lanes. Point3 is six words and lanes under XxHash32, three
  serial tail rounds under XxHash64.
- Where xxHash64 does use lanes it merges them with four extra rounds xxHash32 does not have.
- A wide leaf outside a member stream (a dictionary's decimal value, a small struct's public `GetHashCode`) is a nested
  `DeepEqualsHashCode64.Hash`, so it pays a second finalization.
- **A store-forwarding stall on decimals**, the largest single cost. The JIT keeps a copied decimal's fields in
  registers, spills them as 32-bit stores and `DecimalLo64`/`DecimalHi64` reload them as one 64-bit load, which the
  processor cannot forward. A probe on net8.0 (`D:\Temp\deepequals\hashprobe`) measured Money's public hash at
  63 ns under XxHash64 against 26 ns under XxHash32; building each 64-bit word from two 32-bit reads with `Pack`
  brought it to 29 ns, and the member-stream form to 24.7 ns against 24.3 ns. §9.1 has the full probe and the
  large-object measurements that followed.

**Cycle handling**, net10.0: TreeNode 585 nodes compares in 10.8 µs under `Graph`, 5.1 µs under `Path`, 2.2 µs under
`Tree` (built-in 1.8 µs); `Tree`'s full hash costs 3.4 µs where the shallow hash costs 26 ns. A 100,000-node chain
compares in 2.8 ms, 3.3 ms and 0.16 ms. The diamond with eight shared levels costs 0.42 µs under `Graph`, 58 µs under
`Path` and 15 µs under `Tree`: the exponential case in §5, measured. The ninth retained pair spills `Graph`'s table and
adds about 300 ns; `Path` stays inline.

**Fingerprints and matching**, net10.0: the S1 sets compare in 86 µs at `MatchingHashDepth = 1` with a wide cap, 5.3 µs
at 2 and 16 µs at 4 (noisy). A set of 100 keys with 100 colliding compares in 10.8 µs against 0.21 µs with none.

**Bit blocks** hold on every runtime, the browser interpreter included: `double[]` and `Point3[]` of 1,000 compare in
1–2% of the built-in time in the browser and hash in 4–8%, so the byte path stays on for WebAssembly (§5).

**Generator scale**, net10.0:

| Declared types | Files | One root | Every type a root |
|---|---|---|---|
| 100 | 190 | 114 ms, 50 MB | 115 ms, 54 MB |
| 1,000 | 1,834 | 1.65 s, 499 MB | 1.76 s, 769 MB |
| 2,000 | 3,663 | 5.3 s, 1.1 GB | 6.1 s, 2.2 GB |

Time grows faster than linearly. At this run every type file's header repeated the registered roots, so generated text
grew with files times roots; step 14 fixed that, bringing the 2,000-class closure to 25.3 MB with one root or every
type a root (§9.3), and step 21 to 19.5 MB (§9.4). A cancellation 20 ms in returns after about 51 ms.

**Findings from this run**, and what became of them:

1. The decimal store-forwarding stall under `XxHash64`. Moot: `XxHash64` is removed (step 13, §9.1). The same stall
   in decimal equality was found and fixed later (step 16, §9.2).
2. Type-file headers list every registered root. Resolved by step 14 (§9.3).
3. Generation time grows faster than linearly with closure size. Open; the O2 worklist rework remains the lead.
4. Generated equality on the cyclic `TreeNode` is 5.6 times the built-in, which does not track cycles. By design:
   `Tree` is the mode for data that cannot hold cycles.

---

## 9. Measurements after step 11

Every probe below is a BenchmarkDotNet project in `D:\Temp\deepequals` (4 warmups and 12 iterations, in process),
run on the same Windows x64 machine, a 12th-generation Intel Core i9-12900HK, on .NET 10 and .NET 8. Sub-nanosecond
results are at the harness's floor; BenchmarkDotNet flags some of them as indistinguishable from an empty method.

### 9.1 XxHash64: what was tried, and why it was dropped

**Approach.** An option `Hashing = XxHash64` ran every generated hash core over xxHash64 with 64-bit words and its own
per-process seed, and folded only the public `GetHashCode` to 32 bits (`FoldNonZero`, so an empty collection never
hashed like null). A 64-bit leaf was one word and a 128-bit leaf two; nested hashes and unordered sums carried 64
bits; two 32-bit hashes packed into one word, `((ulong)(uint)a << 32) | (uint)b`, narrow words paired before the
wide ones. The hypothesis: half the words, so about half the rounds, with each round one instruction on 64-bit
processors and in WebAssembly.

**Step 11**, the scenario harness (§8 has the table): slower on .NET 10 and .NET 8 in almost every scenario, up to 2.3
times on `Dictionary<string, decimal>` and 3.3 times on `record struct Money`; faster only on .NET Framework for a few
scenarios and in the browser for the decimal-heavy ones; level on bit-block sequences, which hash through XxHash3 at
either width.

**Why**, from the generated code, the disassembly and a focused probe (`hashprobe`):

- An xxHash64 tail round costs three multiplies where xxHash32's costs two, and a 64-bit multiply is no cheaper on
  x64. Packing pushes most objects under four words, where both hashes run serial tail rounds instead of four
  parallel lanes: Point3 is six words and lanes under XxHash32, three serial rounds under XxHash64.
- Where xxHash64 does use lanes it merges them with four extra rounds xxHash32 has no counterpart for.
- A wide leaf outside a member stream was a nested hash with its own finalization.
- A store-forwarding stall on decimals, the largest single cost. Probe on .NET 8, Money's hash:

| Shape | XxHash32 | XxHash64 | XxHash64, words from two 32-bit reads |
|---|---|---|---|
| public `GetHashCode`, decimal as a nested hash | 26.3 ns | 64.7 ns | 29.0 ns |
| member stream, decimal as words | 24.9 ns | 33.2 ns | 24.7 ns |
| a decimal alone | 7.2 ns | 6.4 ns | |
| three doubles | 11.3 ns | 11.4 ns | |

**Large objects.** Asked whether it paid off on wide objects, the last commit with XxHash64 was checked out into a
scratch worktree with the decimal readers patched as above, and objects of 8 to 24 properties were timed (`probe64`):

| Object | .NET 10, XxHash32 / XxHash64 | .NET 8, XxHash32 / XxHash64 |
|---|---|---|
| 12 `int` | 4.35 / 7.78 ns | 4.25 / 7.63 ns |
| 24 `int` | 9.91 / 10.24 ns | 9.75 / 9.78 ns |
| 12 `double` | 9.93 / 8.09 ns | 9.74 / 8.02 ns |
| 16 `long` | 13.18 / 9.65 ns | 13.14 / 9.42 ns |
| 12 mixed: 3 strings, 3 ints, 2 longs, double, decimal, DateTime, Guid | 22.54 / 24.40 ns | 22.00 / 24.47 ns |
| 24 mixed | 47.60 / 45.74 ns | 47.22 / 46.59 ns |
| Customer, 8 properties | 15.03 / 17.94 ns | 15.39 / 18.64 ns |
| `record struct Money` | 5.04 / 7.34 ns | 5.45 / 17.34 ns |

XxHash64 won 19 to 27% only where most fields are `long` or `double`, lost up to 79% where narrow fields dominate, and
came within a few nanoseconds either way on realistic objects, where the string hash dominates. On .NET 8 the patched
decimal readers still stalled, because the JIT copied the decimal with one 16-byte vector store and the 4-byte reads
from it do not forward.

**Decision:** dropped in step 13. The gain was a few nanoseconds on numeric-heavy types, against a second code path
through every hash core.

### 9.2 Equality at least as fast as a record's own `Equals`

**How the runtime does it**, from the .NET 10 disassembly of `record struct Money(decimal Amount, string Currency)`:
the record's `Equals` inlines into its caller; `EqualityComparer<decimal>.Default` devirtualizes to `decimal.Equals`,
which reads the backing field's `_flags`, `_hi32` and `_lo64` at their own widths and calls `VarDecCmpSub` only for two
nonzero values of the same sign; the string compare inlines to reference, null and length checks and one
`SequenceEqual`.

**The decimal compare alone was not the problem.** `DecimalEquals` (two 64-bit compares) against the alternatives,
reading through the record struct's property as generated cores did, .NET 10:

| Compare | Time |
|---|---|
| `DecimalEquals`, two 64-bit reads | 1.00 ns |
| four 32-bit reads | 1.03 ns |
| `MemoryMarshal.AsBytes(...).SequenceEqual` | 0.75 ns |
| `Vector128` equality | 0.96 ns |
| `decimal.Equals`, numeric | 2.59 ns |
| the record's whole `Equals` | 2.20 ns |
| `DecimalEquals` on a class field, in place | 0.05 ns |

**The comparer around it was.** The generated `MoneyEqualityComparer.Equals` took 3.97 ns against the record's 2.38 ns,
and 5.67 ns through `IEqualityComparer<Money>`. Its disassembly: the JIT inlined it and kept both Money values in
registers, then, because `DecimalEquals` takes the decimal's address, zeroed two stack slots, stored the fields back
as 32- and 64-bit pieces, and read the first eight bytes of each as one 64-bit load spanning two 32-bit stores: a
store-forwarding stall per operand. The record's code spills too but reads every field back at the width it stored.

**What fixes it.** Every compare shape, with the decimal copied through the getter or read in place through
`[UnsafeAccessor]`, and with and without `AggressiveInlining` on the comparer:

| Shape | .NET 10 | .NET 8 |
|---|---|---|
| record `Equals` | 2.00 ns | 2.55 ns |
| generated, getter, two 64-bit reads (as shipped) | 3.53 ns | 4.65 ns |
| getter, runtime field widths (int, int, long) | 0.61 ns | 4.95 ns |
| getter, `Unsafe.BitCast` to `UInt128` | 3.31 ns | 3.12 ns |
| in place, two 64-bit reads | 0.44 ns | 0.34 ns |
| in place, field widths | 0.82 ns | 0.64 ns |
| inlined, getter, field widths | 0.61 ns | 0.64 ns |
| inlined, in place, two 64-bit reads | 0.67 ns | 0.35 ns |
| record through `IEqualityComparer<T>` | 3.26 ns | 2.64 ns |
| in place, field widths, through `IEqualityComparer<T>` | 1.57 ns | 1.44 ns |

Field widths win only when the JIT keeps the fields in registers (.NET 10); on .NET 8 the copy is one vector store and
the same reads stall. Reading in place is fast on both, whatever the compare. Step 16 therefore reads every
auto-property of a value type wider than a register through `[UnsafeAccessor]`, reaches a nullable's struct or decimal
payload by reference, and inlines stateless struct comparers. Money after the change: .NET 10 0.71 ns direct and 1.42
ns through the interface, against 2.19 and 3.04 for the record; .NET 8 1.29 and 1.57 against 2.63 and 2.80.

**The record models**, with the downstream data (distinct string instances), generated against the record, direct and
through `IEqualityComparer<T>`:

| Model | Decimal in place only (.NET 10) | Final, .NET 10 | Final, .NET 8 |
|---|---|---|---|
| Money | 3.19 → 1.52; 4.14 → 2.38 | 3.85 → 1.93; 4.61 → 2.90 | 4.40 → 3.09; 5.07 → 3.68 |
| Invoice | 10.6 → 9.1; 11.4 → 10.1 | 11.1 → 9.0; 11.7 → 9.5 | 14.4 → 14.9; 15.4 → 14.8 |
| Person | 11.4 → 16.4; 11.8 → 17.1 | 11.5 → 9.6; 12.3 → 9.6 | 14.2 → 13.9; 15.3 → 13.8 |
| Point3 | 0.33 → 0.51 | 0.37 → 0.27 | 0.36 → 0.52 |

Each cell is record → generated in nanoseconds, direct then through the interface. Person was slower until struct
members were read in place: through getters each `x.Position.X` copied the whole `Point3` and each `x.Balance` access
the whole `Money?`. Point3 alone compiles to the same six loads and three compares on both runtimes and moves below
the harness's resolution from run to run. Reading doubles in place too was tried: the JIT emitted the identical load,
and on .NET 8 Point3 lost its use-site inlining (1.38 ns against 0.30), so it was reverted.

The scenario harness, which calls through delegates over captured copies and adds a few nanoseconds of its own, after
the change on .NET 10: Money 2.51 → 2.19 ns (Step 11: 1.75 → 3.65), Invoice 11.9 → 9.9, Person 11.4 → 10.1, Customer
7.6 → 5.4, Order 252 → 218, Point3 1.39 → 1.67.

### 9.3 Generated size after the header changes (step 14)

| Declared types | Files | Before, one root / every type a root | After, one root / every type a root |
|---|---|---|---|
| 100 | 190 | 1.74 / 2.00 MB | 1.32 / 1.32 MB |
| 1,000 | 1,834 | 16.7 / 44.0 MB | 12.6 / 12.6 MB |
| 2,000 | 3,663 | 33.4 / 146 MB | 25.3 / 25.3 MB |

Output no longer depends on the number of roots: every type file used to repeat the registered roots in its header.

### 9.4 Generated size after aliases, the one-line notice and file-scoped namespaces (step 21)

Where the bytes went before step 21, in the downstream models' 144 files (1,079,928 characters, C# 14): `global::`
prefixes 34.2% (`DeepEqualsHelpers.` alone 9.3%, `DeepEqualsHashCode.` 4.6%), indentation 15.8%, headers 6.8%, XML
documentation 3.9%. Folding the reference prelude of `Equals_T` into one expression was measured and rejected: only 13
of 111 preludes are a bare prelude and a return, the rest carry guards or dispatch, so it saves 0.1%; and the net10 JIT
compiles both forms to the same instructions (40 and 17 instructions, 11 and 3 branches, for inlined and called member
chains), only laying the blocks out in a different order. `b ? a : c` and `if (b) return a; return c;` both compile to
one `cmov`: the JIT picks a conditional move from the flow graph, whichever way the source is written.

| Sample | Before | After | Change |
|---|---|---|---|
| downstream models, 144 files, C# 14 | 1,079,928 chars | 864,273 chars | −20.0% |
| scale closure, 2,000 classes, 3,663 files, one root / every type a root | 25.3 / 25.3 MB (step 14) | 19.5 / 19.5 MB | −23% |
| scale closure, 1,000 classes | 12.6 / 12.6 MB (step 14) | 9.7 / 9.7 MB | −23% |
| scale closure, 100 classes | 1.32 / 1.32 MB (step 14) | 1.01 / 1.02 MB | −23% |

The scale closure's "before" is the step 14 measurement, so its change also covers steps 15 to 20; the downstream
sample was measured immediately before and after step 21.

