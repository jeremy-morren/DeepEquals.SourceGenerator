# Update plan: cycle handling and 64-bit hashing

Two new per-context options on `[DeepEqualsSourceGenerationOptions]`, applied on top of the `splitFiles` branch.

| Option | Values | Default | What it changes |
|---|---|---|---|
| `CycleHandling` | `Graph`, `Path`, `Tree` | `Graph` | How a comparison remembers where it has been, and therefore what a cyclic type costs |
| `Hashing` | `XxHash32`, `XxHash64` | `XxHash32` | The width of the hash stream every generated hash core runs |
| `MaxDepth` | 1 .. 1,000,000 | 512 | `Tree` only: the guarded nesting depth past which a comparison throws |

Dropped from the earlier brainstorm, on purpose: a stable hash mode and a declared-type polymorphism mode. Users seal
classes for dispatch performance; the README will say so.

Both options are per context only. A per-root override would need two variants of every shared core, which is not
worth it.

---

## 1. `CycleHandling`

### 1.1 What each value means

| Value | Guard at a cyclic core | State | Real cycles | Shared subgraphs (DAG) | Hash |
|---|---|---|---|---|---|
| `Graph` (today) | `state.TryEnter(kind, x, y)`; a pair already seen is equal | `DeepEqualsState`, retained for the whole call | Handled, coinductive | Each pair compared once | Levels: one payload edge into a cycle, then shallow |
| `Path` | `TryEnter`, then `Rollback` to the mark when the core returns | `DeepEqualsState`, holds only the ancestors of the current pair | Handled, coinductive | Re-compared per path; can go exponential on heavy sharing | Levels, as `Graph` |
| `Tree` | `if (++depth > MaxDepth) throw`, plus the execution-stack check | One `int depth` by value | Throw `DeepEqualsComplexityException` | Re-compared per path | Full walk, bounded by `MaxDepth`; no levels, no shallow cores |

`Tree` is the option for deserialized API models: no pair table, no pool rentals, no pair budget, and a hash that sees
the whole tree instead of one level. `Path` is the middle ground for graphs that are almost trees.

Semantics under `Graph` and `Path` are identical. `Tree` differs in two visible ways: a real cycle throws instead of
comparing equal, and hash values differ because the whole tree takes part. Hash values are already per process, so
that is a documentation note, not a break.

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
    if (++depth > MaxDepth) DeepEqualsHelpers.ThrowDepthExceeded(MaxDepth);
    global::System.Runtime.CompilerServices.RuntimeHelpers.EnsureSufficientExecutionStack();
    return EqualsMembers_Node(x, y, depth);
}
```

`depth` is by value, so callees see the incremented copy and nothing is decremented on return or on exception. The
stack check stays in the guard because `MaxDepth` is a cycle detector, not a stack bound: a real overflow surfaces as
`InsufficientExecutionStackException`, never a crash.

The hash under `Tree` takes the same parameter, has the same guard, and calls `GetHashCode_T` for every edge. No
`ShallowHashCode_T` or `ShallowHashMembers_T` is emitted; `HasShallowHash` is forced false in the model.

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

The hash of such a type under `Tree` also has to loop, since it now walks the chain: one `Streaming` (or
`Streaming64`) over the non-tail members per node, with the same Brent guard.

**Unordered collections.** A set or dictionary whose entry closure is cyclic today calls the stateful
`DeepEqualsUnordered.SetEquals` with mark and rollback per trial. Under `Tree` there is no state to roll back, so the
framework gets depth overloads and a depth ops interface:

```csharp
public interface IDeepEqualsDepthElementOps<in T> : IDeepEqualsHashOps<T> { bool Equals(T x, T y, int depth); }
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

`EqualsExact_T` and the boxed adapter get the same wrap. Tail loops keep `TryEnter` per iteration, since the chain
is the path; `Rollback` to the mark taken before the loop runs once after it. Sets of cyclic elements use the existing
stateful ops unchanged. The state stays at depth-many pairs, so for most data it never spills past the 8 inline slots.
`MaxComparisonPairs` now bounds depth rather than total pairs, and the header comment says so.

### 1.4 Files

Framework, `src/DeepEquals.SourceGeneration.Framework/`:

- `Attributes.cs`: `enum DeepEqualsCycleHandling { Graph, Path, Tree }`, the `CycleHandling` and `MaxDepth`
  properties with `DefaultMaxDepth = 512` and `MaximumMaxDepth = 1_000_000`, XML docs stating the semantics above.
- `Exceptions.cs`: two `DeepEqualsComplexityException` constructors and properties, `Depth`/`MaxDepth` for the depth
  bound and a cycle form carrying the type, so a `Tree` context reports which type closed the loop.
- `DeepEqualsHelpers.cs`: `ThrowDepthExceeded(int maxDepth)` and `ThrowCycle(Type type)`, both `NoInlining`, so the
  guard stays one compare and one branch.
- `DeepEqualsOps.cs`: `IDeepEqualsDepthElementOps<T>`.
- `DeepEqualsUnordered.cs`: `SetEquals` and `DictionaryEquals` overloads taking `int depth` through the depth ops,
  sharing the materialization and matching code with the stateless overloads (the trial loop without mark/rollback).
- `DeepEqualsState.cs`: no change. `Count`, `Mark`, `Rollback` already support `Path`.

Generator, `src/DeepEquals.SourceGenerator/`:

- `Model/ContextOptions.cs`: `CycleHandling` and `MaxDepth` fields and defaults.
- `Analysis/OptionsReader.cs`: reads both, validates the enum against its defined values and `MaxDepth` against its
  range, falls back with `DEQ013` like every other option.
- `Analysis/CapabilityProbe.cs` and `Model/TargetCapabilities.cs`: `HasFrameworkStrategies`, true when the resolved
  framework asset has `IDeepEqualsDepthElementOps`. A `Tree` or `XxHash64` request against an older asset warns
  `DEQ013` and uses the default.
- `Analysis/ModelBuilder.cs`: `HasShallowHash` becomes `cyclic && options.CycleHandling != Tree`. Guard selection,
  `NeedsState`, tail members and boxed guards are unchanged; `Tree` reuses `NeedsState` as "needs depth".
- `Emit/Emitter.cs`:
  - `StateParam`/`StateArg` switch on the option: `ref DeepEqualsState state` or `int depth`.
  - `EmitCycleGuard` writes the `TryEnter` line, the `TryEnter`+mark line, or the depth line.
  - `EmitClassCores`, `EmitBoxedAdapter`, `EmitProductCores`, `EmitArrayOrListCores`, `EmitListInterfaceCores`,
    `EmitEnumerableCores`: guarded bodies under `Path` are split into wrapper and body so that `Rollback` runs on
    every return; tail loops get the Brent guard under `Tree`.
  - `EmitUnorderedCores`: picks the depth ops interface and the depth overloads under `Tree`.
  - `EmitMembersHash`, `EmitDispatchHash`, `HashOrOmit`: under `Tree`, level is always 1, the depth parameter is
    threaded, the guard is emitted, and tail-member types hash in a loop.
  - `EmitWrapper`: no state declaration, `try`/`finally` or `Dispose` under `Tree`; passes `0`.
  - `EmitConstants`: `MaxDepth` constant under `Tree`.
  - `HeaderValues`: three new "Options in effect" lines.
- `Templates/AutoGeneratedHeader.cs`: the three placeholders.
- `KnownTypes.cs`: the new framework names.

---

## 2. `Hashing = XxHash64`

### 2.1 Design

A 64-bit xxHash64 stream in place of the 32-bit one, with the same process seed policy, so every generated hash core
returns `ulong` and only the public `GetHashCode(T)` folds:

```csharp
public int GetHashCode(Customer? o) => DeepEqualsHashCode64.Fold(GetHashCode_Customer(o));
// Fold(h) = (int)(h ^ (h >> 32))
```

Words are 64-bit. Each leaf contributes as follows, and the emitter packs narrow words in pairs:

| Leaf | 32-bit words today | 64-bit words |
|---|---|---|
| `int`, `uint`, `bool`, `char`, small enums, `float`, `Half`, `string`, `Uri`, custom comparer, `[SimpleType]`, default `.GetHashCode()` | 1 | narrow; two per word: `Pack(a, b) = ((ulong)(uint)a << 32) \| (uint)b` |
| `long`, `ulong`, `nint`, 8-byte enums, `double`, `DateTime`, `TimeSpan` | 2 | 1 |
| `decimal`, `Guid`, `DateTimeOffset`, `Int128`, `UInt128` | 4 | 2 |
| nested object, collection, product | 1 | 1, carrying all 64 bits |

Packing rules:

- Narrow words of one owner are paired in declaration order, then the wide words follow. Wide words are never
  interleaved with the pairs, so no half-empty word is wasted on a wide leaf sitting between two narrow ones.
- An odd trailing narrow word sits alone with a zero high half. Arity is fixed per type, so nothing is ambiguous.
- The cast goes through `uint`. A negative `int` cast straight to `ulong` sign-extends over the high half.
- Nested hashes and unordered sums are 64-bit, so sibling collisions and set-sum collisions drop from 2^-32 to 2^-64.

xxHash64, not XXH3 or wyhash: its rounds are 64-bit multiply, add and rotate, each one `i64` instruction in
WebAssembly. The faster hashes need a widening 64x64 multiply, which wasm lacks. The 32-bit option stays for x86
processes.

Unchanged: the seed policy (a separate 64-bit process seed, settable from the test assembly), the empty-collection
rule (0 for null, nonzero for empty), the level machinery under `Graph` and `Path`, and the matching hashes inside
`DeepEqualsUnordered`, which keep their packed 32-bit `(hash, index)` keys and receive the folded value.

### 2.2 Files

Framework:

- New `DeepEqualsHashCode64.cs`: primes, `s_seed64`, `Seed` (internal), `Round`, `MergeRound`, `MixFinal`,
  `Fold`, `Pack`, `Hash(in decimal)`, `Hash(Guid)`, `Hash(Int128)`/`Hash(UInt128)` as two words,
  `HashSpan64<T, TOps>` over `IDeepEqualsHashOps64<T>`, and `Streaming64` with `Add(ulong)` and `ToHashCode()`
  returning `ulong`. Strings stay narrow through `DeepEqualsHashCode.Hash(string?)`.
- New `DeepEqualsHashCode64.Combine.g.cs`: `ulong Combine(ulong h1 .. hN)` for `N` 1 to 32.
- `eng/Generate-HashCodeCombine.ps1`: a `-Width 64` switch that emits the 64-bit lane arithmetic and the second
  file; the 32-bit output is byte-identical to today.
- `DeepEqualsHelpers.cs`: `DecimalLo64`/`DecimalHi64` and `GuidLo64`/`GuidHi64` next to the existing single-word
  readers; `DateTimeOffsetTicks` is not needed, the emitter reads `.Ticks` and `.Offset.Ticks`.
- `DeepEqualsOps.cs`: `IDeepEqualsHashOps64<T> { ulong GetHashCode64(T x); }`.
- `DeepEqualsUnordered.cs`: no change; the 64-bit container hash is generated inline, the matching path folds.

Generator:

- `Attributes.cs`: `enum DeepEqualsHashing { XxHash32, XxHash64 }` and the `Hashing` property.
- `Model/ContextOptions.cs`, `Analysis/OptionsReader.cs`, header template: as for cycle handling.
- `Emit/Emitter.cs`:
  - `HashWords`, `WideLeafWords`, `Words64`: the word list becomes `List<HashWord>` with `(string Expr, bool Wide)`.
    Under 64-bit a wide leaf adds one or two `ulong` expressions; under 32-bit behaviour is unchanged.
  - `Combine(List<...>)`: under 64-bit, pairs narrow words with `Pack`, casts a lone narrow word, and nests above
    32 words as today.
  - `LeafHash`: unchanged expressions, all narrow. Wide leaves reached outside a member stream (a list element, a
    dictionary key) use the 64-bit `Hash` overloads.
  - `EmitMembersHash`, `EmitDispatchHash`, `CaseHash`, `EmitProductCores`, `EmitValueSequenceCores`,
    `EmitSpanHash`, `EmitEnumerableCores`, `EmitUnorderedCores`, `EntryHash`, `EmitHashOps`: return type `ulong`,
    `ulong sum`, `HashSpan64`, `Streaming64`, and the 64-bit ops struct.
  - `EmitWrapper`: `Fold` around the top-level hash.
  - A `HashClass`/`HashType`/`Fold` trio on the emitter so the two widths share every code path.
- `KnownTypes.cs`: `GlobalHashCode64`, `GlobalHashOps64`.

---

## 3. Tests

### 3.1 Framework tests, `tests/DeepEquals.SourceGeneration.Framework.Tests/`

New `HashCode64Tests.cs`, mirroring `HashCodeTests.cs` with an independent naive xxHash64 reference:

- `Combine64_matches_reference_for_every_arity` (theory over three seeds, arity 1 to 32, 20 trials each).
- `Empty_stream_with_seed_zero_is_the_published_xxHash64_vector` (`0xEF46DB3751D8E999`).
- `Empty_stream_never_returns_zero`.
- `Streaming64_lengths_around_lane_boundaries_match_reference`.
- `HashSpan64_matches_streaming_and_combine`.
- `Fold_uses_both_halves`: two values differing only above bit 32 fold differently.
- `Pack_casts_through_uint`: a negative low word leaves the high word intact, and `Pack(a, b) != Pack(b, a)`.
- `Decimal_and_guid_hashes_use_both_words`: distinct scales and the six bytes `Guid.GetHashCode` folds away.
- `Seed64_is_independent_of_the_32_bit_seed`.

`HelpersTests.cs`, added:

- `Decimal_and_guid_64_bit_words_match_the_32_bit_words`: the lo/hi readers agree with the single-word readers.
- `ThrowDepthExceeded_and_ThrowCycle_carry_their_arguments`.

`UnorderedTests.cs`, added:

- `Depth_sets_of_cyclic_elements_compare_equal`: the depth overloads over the same data as
  `Stateful_sets_of_cyclic_elements_compare_equal_and_trials_roll_back`.
- `Depth_overloads_throw_past_the_bound`.
- `Exact_matching_agrees_with_brute_force_on_every_matrix_and_permutation`: extended to the depth overloads.

`StateTests.cs`, added:

- `Enter_then_rollback_per_level_never_spills_for_a_deep_chain_of_ancestors_under_eight`: the `Path` pattern stays
  inline. The existing rollback tests already cover the spilled case.

### 3.2 Generator tests, `tests/DeepEquals.SourceGenerator.Tests/`

New `CycleHandlingTests.cs`:

- `Tree_emits_depth_parameters_and_no_state`: source assertions, no `DeepEqualsState`, no `try`, `int depth`,
  `MaxDepth` constant, no `ShallowHashCode_`.
- `Tree_throws_on_a_real_cycle_and_names_the_type`: `a.Next = a` throws `DeepEqualsComplexityException` with the
  type; two-node cycle likewise; the same data under `Graph` and `Path` is equal.
- `Tree_max_depth_is_honoured`: a nested chain of 5 with `MaxDepth = 3` throws, of 3 passes; `MaxDepth = 0`
  and an undefined enum value each report `DEQ013` and use the default.
- `Tree_linked_lists_loop_and_detect_cycles_with_brent`: 200,000-node chains equal and unequal without depth
  growth; a rolled list throws; the hash of the chain completes.
- `Tree_hash_walks_the_whole_tree`: two trees that differ three levels down hash differently under `Tree` and
  identically under `Graph`.
- `Tree_sets_and_dictionaries_of_cyclic_typed_elements_use_the_depth_overloads`: source and behaviour.
- `Tree_boxed_struct_cycle_through_object_throws`: the `Tree` counterpart of
  `Boxed_struct_cycle_through_object_terminates`.
- `Path_terminates_on_cycles_and_unrolled_cycles_are_equal`: the fixture cycle test under `Path`.
- `Path_rolls_back_on_every_return`: source assertion that each guarded core has one `Rollback`; behaviour on a
  tree where a subtree compares unequal early.
- `Path_shared_subgraphs_are_recompared_and_still_correct`: a diamond DAG.
- `Path_tail_loops_rollback_after_the_loop`.
- `Every_strategy_agrees_on_acyclic_data`: theory over the three values against one acyclic model set, equal and
  unequal pairs, both hash widths.

New `Hashing64Tests.cs`:

- `XxHash64_cores_return_ulong_and_the_wrapper_folds`: source assertions.
- `Narrow_leaves_pack_in_pairs_after_wide_words`: a type with three ints and a double emits `Pack` once, a lone cast
  once, one `DoubleToInt64Bits` word, and a `Combine` of arity 3.
- `Equal_values_hash_equal_and_agree_with_equality_under_XxHash64`: class, struct, inlined struct, nullable, tuple,
  array, list behind interface, immutable array, set, dictionary, dispatch, and the wide leaves with the same edge
  data as `Wide_leaves_hash_by_word_and_agree_with_equality`.
- `XxHash64_collections_use_HashSpan64_Streaming64_and_a_ulong_sum`: source assertions.
- `XxHash64_custom_comparers_and_simple_types_are_narrow_words`.
- `XxHash64_falls_back_with_DEQ013_on_an_older_framework_asset`: same gating pattern as `SpanGateTests`.
- `Int128_hashes_as_two_words_under_XxHash64`.

Changed:

- `HashLevelTests.Nested_lists_tuples_and_dictionaries_in_a_cycle_terminate_and_hash_consistently`: theory over
  `{Graph, Path} x {XxHash32, XxHash64}`.
- `FastPathTests.Wide_leaves_hash_by_word_and_agree_with_equality` and
  `One_guard_per_cycle_still_terminates_and_equates_rolled_and_unrolled_graphs`: theory over both hash widths, and
  the guard test over `Graph` and `Path`.
- `StrategyAndDiagnosticTests.Diagnostics_are_reported`: `DEQ013` cases for the three new options.
- `BasicGenerationTests.Output_is_one_context_file_plus_one_file_per_emitted_type`: header shows the three lines.
- `IncrementalTests`: `Changing_an_option_reruns_the_output` for `CycleHandling` and `Hashing`.

### 3.3 Fixture tests, `tests/DeepEquals.Fixtures.Tests/`

`Models.cs` gains `PathFixtureContext`, `TreeFixtureContext` (registering the acyclic-data models and `Node`) and
`Hash64FixtureContext` over the same models. `FixtureTests.cs`:

- `Cycles_terminate_and_unrolled_cycles_are_equal` runs under `Graph` and `Path`.
- `Tree_context_throws_on_cycles`.
- `Long_chains_use_one_stack_frame` runs under all three.
- `Hash64_context_hashes_equal_values_equal_on_every_fixture`.
- `Holder_covers_structs_nullables_enums_collections_and_products` runs under both hash widths.

### 3.4 Downstream, `downstream/`

- `models/Contexts.cs`: `DownstreamPathContext` and `DownstreamTreeContext` registering `TreeNode`, and
  `DownstreamHash64Context` registering every root. `GraphNode` stays out of the tree context.
- `models/Scenarios.cs`: `Generated64Hash` and `Generated64HashOfOther` delegates on every scenario, and two new
  scenarios, "TreeNode x585 (Path)" and "TreeNode x585 (Tree)", against the same built-in.
- `models/Checks.cs`: the 64-bit context agrees with equality on every scenario; the tree context throws on the
  graph fixture; the path context equates rolled and unrolled graphs.
- `models/MicroBench.cs`: a "hash64 gen" column and ratio.
- `benchmarks/DeepEquals.Benchmarks/EqualityBenchmarks.cs`: `Generated64_GetHashCode` in the hash category.
- `smoke/consumers/BlazorWasm/Result.razor`, `Consumer/Program.cs`: print the new column; the smoke tests assert
  the new checks pass and log the browser table with the new column.

### 3.5 Docs

- `README.md`: the options table gains three rows, a paragraph per `CycleHandling` value, and the sealing note.
- `docs/Implementation.md`: §3 (64-bit stream and packing rule), §6.1 and §6.7 (tail loops under `Tree`), §6.8
  (depth overloads), §7 (the three modes, what `Path` rolls back, what `Tree` throws), §8 (`DeepEqualsHashCode64`),
  §11 (the strategies capability), §14 (dropped modes).
- `docs/Diagnostics.md`: `DEQ013` lists the new option names.

---

## 4. Order of work

1. Framework 64-bit hashing: generator script, `DeepEqualsHashCode64`, helpers, ops, framework tests. Nothing in the
   generator changes yet, so every existing suite stays green.
2. Emitter 64-bit hashing behind the option, `Hashing64Tests`, the changed theories, fixture context.
3. Framework depth overloads and exceptions, `UnorderedTests` additions.
4. `Path` in the emitter, `CycleHandlingTests` for `Path`, fixture context.
5. `Tree` in the emitter: depth threading, tail-loop Brent guard, full hash walk, depth ops; the `Tree` tests.
6. Options plumbing, capability probe, header, `DEQ013` cases, incremental test.
7. Downstream contexts, scenarios, checks, benchmark method, consumer output, smoke assertions.
8. Docs.
9. Repack and run: generator tests, framework tests on five frameworks, fixtures, smoke, then the benchmarks on
   net472, net8.0 and net10.0 plus the browser table. Not before you say so.

Rough effort: one day for hashing, one and a half for cycle handling, half a day for downstream and docs.

## 5. Risks and decisions taken

- **`Tree` on a real cycle throws.** That is the point of the option. The exception names the type, and the README
  tells users with in-memory graphs to keep `Graph`.
- **`MaxDepth` default 512.** System.Text.Json stops reading at 64. Linked lists do not count against it because the
  tail loop keeps depth flat. The stack check stays in the guard, so a smaller thread stack degrades to an
  exception rather than a crash.
- **`Path` on heavily shared DAGs can be exponential.** Documented; `MaxComparisonPairs` no longer caps total work
  there, only depth. Users with such data keep `Graph`.
- **Hash values change with the mode.** Already true across processes; the docs state it once more.
- **Older framework asset.** Either new option against an asset without the new types warns and falls back, so a
  generator upgrade without a framework upgrade never produces code that fails to compile.
- **Matching hashes stay 32-bit** inside `DeepEqualsUnordered`, so its packed keys and collision-run logic are
  untouched; only the container's own hash and the entry hashes fed to it are 64-bit.
