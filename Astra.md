# Repository and UpdatePlan audit

Audited 11 September 2026, branch `splitFiles`, commit `f50ddb9`. This report reviews the current implementation and the proposed changes in `UpdatePlan.md`; proposed features are not treated as already implemented.

**The existing tests pass, but five generator defects reproduce outside their coverage. The plan also needs changes to depth propagation, compatibility, and cycle semantics before implementation.** The most consequential runtime scenario found is a collision-limit failure on two equal sets of 65 ordinary, acyclic linked chains.

Priority: **P1** should be resolved before implementing or shipping the affected feature; **P2** is a targeted correctness, usability, or performance follow-up. Performance opportunities below are source-based assessments, not measured speedup claims.

## Validation performed

- `dotnet test DeepEquals.SourceGenerator.slnx --no-restore --verbosity quiet`: **398 passing test executions**. Framework: 49 tests on each of net472, netcoreapp3.1, net6.0, net8.0, and net10.0. Fixtures: 7 on each of those targets. Generator: 59 on each of net8.0 and net10.0.
- Eight additional audit probes ran on both generator-test targets: **all eight exposed the expected failures on both targets**. Seven probes cover the five generator defects below; the eighth demonstrates the documented collision limit on unexpectedly ordinary data.
- Temporary probe tests were removed from the test project. Their source and detailed output remain locally in `artifacts/AuditProbeTests.cs` and `artifacts/audit-probes.log` (ignored build artifacts). The minimal reproducers are also recorded below.
- Reviewed generator analysis/emission, runtime hashing/state/unordered matching, tests, downstream benchmark/smoke setup, documentation, and the complete update plan. Package smoke tests, browser/Mono/AOT runs, and performance benchmarks were not rerun. No implementation fixes are included in this audit.

## Confirmed implementation defects

### B1 — P1: Escaped context identifiers crash source publication

**Evidence:** [ContextAnalyzer.cs](src/DeepEquals.SourceGenerator/Analysis/ContextAnalyzer.cs), lines 69–75; [Emitter.cs](src/DeepEquals.SourceGenerator/Emit/Emitter.cs), lines 320–326; [DeepEqualsGenerator.cs](src/DeepEquals.SourceGenerator/DeepEqualsGenerator.cs), line 101.

This valid declaration produces CS8785 and no usable generator output:

```csharp
using DeepEquals.SourceGeneration;
[GenerateDeepEquals(typeof(int))]
public partial class @class : DeepEqualsContextBase { }
```

`HintNamePrefixFor` uses the C# display name, including `@`, as a filename. Roslyn rejects `@class.g.cs`. A context inside `partial class @namespace` fails the same way. Publication is outside the emitter's exception handler, so this escapes the promised DEQ099 diagnostic handling.

There is a second issue behind the first: the emitted class declarations use raw symbol names, without the emitter's identifier-escaping helper. Fixing only the filename would expose invalid C# such as `partial class class`.

**Fix:** Separate filename encoding from C# identifier escaping. Encode the complete context identity into a valid hint prefix, and escape context/containing-type names when emitting declarations. Add keyword namespace, keyword context, and keyword containing-type tests that assert both absence of generator exceptions and presence of generated output.

### B2 — P1: Context names differing only by case collide globally

**Evidence:** [Emitter.cs](src/DeepEquals.SourceGenerator/Emit/Emitter.cs), lines 255–269; [ContextAnalyzer.cs](src/DeepEquals.SourceGenerator/Analysis/ContextAnalyzer.cs), line 69.

Two valid contexts named `C` and `c`, each registering `int`, produce CS8785: `The hintName 'c.g.cs' ... must be unique within a generator.` The emitter resolves case-insensitive filename collisions only inside one context, whereas Roslyn requires uniqueness across contexts. This can disable generation beyond the offending context.

**Fix:** Add a case-insensitive check for colliding filenames across the entire project, and append a crc32 hash if there are any.

### B3 — P2: Contexts inside structs and records emit the wrong containing declaration

**Evidence:** [ContextAnalyzer.cs](src/DeepEquals.SourceGenerator/Analysis/ContextAnalyzer.cs), `ValidateDeclaration`; [ModelBuilder.cs](src/DeepEquals.SourceGenerator/Analysis/ModelBuilder.cs), lines 107–111; [Emitter.cs](src/DeepEquals.SourceGenerator/Emit/Emitter.cs), line 322.

```csharp
public partial struct Outer
{
    [GenerateDeepEquals(typeof(int))]
    public partial class C : DeepEqualsContextBase { }
}
```

Validation accepts this, but the model retains only `Outer`'s name and emission writes `partial class Outer`. Compilation fails with CS0261. The same failure reproduced with `partial record Outer`.

**Fix:** Nested contexts are supported. Ensure filename includes outer class and emitted code does as well. 2 partial class errors now: 1 if the context isn't partial, a different one if the parent class (all the way up to the root) isn't partial.

### B4 — P2: A null exclusion-prefix array becomes DEQ099

**Evidence:** [OptionsReader.cs](src/DeepEquals.SourceGenerator/Analysis/OptionsReader.cs), lines 79–85.

```csharp
[DeepEqualsSourceGenerationOptions(ExcludeInterfacesByPrefix = null)]
public partial class C : DeepEqualsContextBase { }
```

This produces DEQ099. The argument still has `TypedConstantKind.Array`, but `Values` is a default immutable array for a null value. Reading its length fails.

**Fix:** Check `IsNull`/`Values.IsDefault` before accessing the array. Treat null as empty or issue DEQ013 and use the default. Add tests for a null array, an empty array, and null elements, including inherited option overrides.

### B5 — P2: Generated hash-member names are missing from collision validation

**Evidence:** [Naming.cs](src/DeepEquals.SourceGenerator/Analysis/Naming.cs), line 147; [ModelBuilder.cs](src/DeepEquals.SourceGenerator/Analysis/ModelBuilder.cs), line 221; [Emitter.cs](src/DeepEquals.SourceGenerator/Emit/Emitter.cs), line 1307.

Register an unsealed `Foo`, then declare `private static int HashMembers_Foo(Foo o) => 0;` in its context. The generator reports no collision diagnostic and emits a duplicate method, producing CS0111 and ambiguous-call errors. The expected diagnostic is **DEQ016**.

`Naming.IdentifiersFor` reserves `GetHashCode_*` and `ShallowHashCode_*`, but omits `HashMembers_*` and `ShallowHashMembers_*`.

**Fix:** Reserve every emitted identifier through one shared naming source. Audit accessor/helper names at the same time. The planned `MaxDepth` constant and any depth-body helper names also need reservations; `UpdatePlan.md` currently omits `Naming.cs` from its file list.

## Reproduced runtime limitation that should influence the plan

### S1 — P1 design input: Hash width does not fix missing distinguishing data

**Evidence:** [Emitter.cs](src/DeepEquals.SourceGenerator/Emit/Emitter.cs), `HashOrOmit` (line 714), `EntryHash` (line 2105); [DeepEqualsUnordered.cs](src/DeepEquals.SourceGeneration.Framework/DeepEqualsUnordered.cs), line 288; `UpdatePlan.md` §2.1.

Use a sealed `Node` with `int Value` and `Node Next`. Build two separate `HashSet<Node>` instances containing these 65 chains:

```text
0 -> 0 -> i -> null       for i = 0 through 64
```

Register `HashSet<Node>` and compare the two sets. The result is `DeepEqualsComplexityException`: 65 entries share a hash, above the default cap of 64. The objects are acyclic, distinct under deep equality, and the sets have corresponding equal elements.

This is consistent with the documented bounded hash and collision cap, rather than an incorrect equality answer. However, it is easy to encounter without malicious data: the type is recursive, so each root hash sees its value and the shallow next-node value, omitting the third node that distinguishes every entry.

**Plan consequence:** `Graph + XxHash64` and `Path + XxHash64` retain those omissions. All 65 hashes will still be identical; a wider mixer cannot recover information never supplied to it. Strings/custom/simple leaves remain 32-bit bottlenecks too, and matching explicitly folds to 32 bits. The claim that sibling and set-sum collision rates become `2^-64` needs qualification to the portions actually carrying independent 64-bit information. Public `GetHashCode` remains 32-bit.

**Action:** Add this exact scenario to tests and benchmarks, including caps 64/65/512 and the Tree mode. Investigate deeper, fixed-depth matching fingerprints or a separately bounded fallback, preserving coinductive hash congruence. Simply increasing the collision cap trades the exception for more matching work.

## UpdatePlan.md findings

### P1 — P1: Depth is missing from hash callbacks

**Evidence:** `UpdatePlan.md` §1.2, lines 61 and 84–90, and §2.3; [DeepEqualsOps.cs](src/DeepEquals.SourceGeneration.Framework/DeepEqualsOps.cs); [DeepEqualsHashCode.cs](src/DeepEquals.SourceGeneration.Framework/DeepEqualsHashCode.cs), lines 148–175; [DeepEqualsUnordered.cs](src/DeepEquals.SourceGeneration.Framework/DeepEqualsUnordered.cs), `FillSet`/`FillDictionary`.

Tree hashing needs the caller's depth, but the proposed `IDeepEqualsDepthElementOps<T>` inherits `GetHashCode(T)` without depth. The proposed 64-bit hash interface also has no depth. Existing span hashing and unordered materialization call `default(TOps).GetHashCode(element)`, so an ops instance cannot capture it either.

Resetting to zero at each collection boundary breaks the depth bound. For example, `Node -> HashSet<Node> -> Node` can repeatedly restart its hashing budget during the materialization that precedes equality matching. A generated callback that refers to a nonexistent `depth` instead fails compilation.

**Amendment:** Specify depth-aware hashing for both widths, span loops, unordered matching fingerprints, and boxed/dispatch adapters. Either pass depth explicitly through the ops APIs or pass an initialized ops value through every caller. Define whether materialization receives the current or incremented depth. Add a cycle through a set/dictionary and an over-depth acyclic object nested inside several collections to the test matrix.

### P2 — P1: The older-framework fallback does not cover the proposed API changes

**Evidence:** `UpdatePlan.md` lines 135–137, 247–254, 275–276, and the final “Older framework asset” decision.

There are three separate compatibility problems:

1. Source using new enum types or new attribute properties cannot compile against an older framework assembly that does not define them. A generator warning cannot remove those ordinary C# binding errors.
2. Even a consumer using only default options would start calling new `FloatBits`/other shims if all emit paths are migrated unconditionally. The old framework asset lacks those methods. Falling back from Tree/XxHash64 alone does not fix that.
3. Removing the existing public netstandard2.0 `SingleToInt32Bits` shim can break already-generated consumer assemblies when they run against the new framework package.

Probing only for `IDeepEqualsDepthElementOps` also does not establish that the 64-bit helpers and required overloads exist.

**Amendment:** Keep existing public helpers as forwarding compatibility shims. Gate new default-path calls against the actual resolved helper surface, or explicitly require a minimum framework version and emit a clear mismatch diagnostic. Separate “new generator with old framework/default source,” “new syntax with old attributes,” and “old compiled consumer with new runtime” tests. Define the compatibility policy before building the new emitter paths.

Dev note: If I understand this is complaining about mismatched nuget packages/built binaries. If so, ignore, this isn't in production yet. If not nuget packages, explain.

### P3 — P1: “Tree throws on real cycles” is stronger than the algorithm

**Evidence:** `UpdatePlan.md` §1.1–1.2 and §3.2, especially lines 32–33, 49, and 65–81.

The shown Tree core first returns true for identical references. Consequently `Equals(a, a)` returns true even if `a.Next = a`. Two roots that share a cyclic child can also return true when comparison reaches the shared reference. An earlier unequal member can return false before a later cycle is visited. Brent detection only on the left side adds operand-order concerns when one traversal is finite and the other cyclic.

The proposed depth test also says a five-node chain should exceed `MaxDepth = 3`, while the tail-loop design deliberately allows a 200,000-node chain without consuming depth. The test must identify a branching or mutually recursive model that actually consumes guarded nesting levels.

**Amendment:** Choose whether Tree validates acyclicity of inputs or only bounds the traversal it performs. The latter fits the fast paths, but must be documented as such. Full validation needs a separate traversal or different shortcuts. Specify the exception contract for non-tail cycles: depth exhaustion alone does not prove which type closed a cycle. Test self-comparison, shared cyclic children, unequal-before-cycle, finite/cyclic operands in both orders, mixed recursive types, and equality versus hashing.

### P4 — P2: Nonzero 64-bit empty hashes can fold to zero

**Evidence:** `UpdatePlan.md` §2.1, lines 165–166 and 190; §3.1 empty-hash tests.

The old empty-collection contract is stated for the public 32-bit hash. Preserving a nonzero internal 64-bit hash is insufficient: `Fold(0x0000000100000001UL) == 0`.

**Amendment:** Define the empty/null normalization at the public fold boundary as well as internally, and apply it consistently to all equivalent collection views. Add a targeted fold-to-zero test; checking only a few random seeds will almost certainly miss this case. Preserve the null hash of zero.

### P5 — P2: Bit-representation tests and packing rules need correction

**Evidence:** `UpdatePlan.md` §2.1–2.2 and §3.1–3.2; [HelpersTests.cs](tests/DeepEquals.SourceGeneration.Framework.Tests/HelpersTests.cs), `Decimal_words_cover_every_GetBits_word_once`.

- Raw decimal storage words need not occur in `decimal.GetBits` order. Existing tests intentionally compare the four words without requiring that order. The plan's two raw 64-bit words cannot simply be compared byte-for-byte with the API's four-word sequence. Compare against the existing raw-word helpers, and separately verify sign/scale/magnitude semantics.
- `decimal.GetBits` is a representation API, not a numeric conversion. Avoiding it for allocation/performance reasons is sensible; banning it as a lossy operation is incorrect and conflicts with using it as the test oracle.
- The unconditional ban on floating/decimal `.GetHashCode()` must exclude explicit SimpleType/custom strategies, which deliberately select user/default semantics.
- The design places paired narrow words before wide words; a proposed test is named `Narrow_leaves_pack_in_pairs_after_wide_words`. Pick one ordering and cover it with deterministic vectors.
- Randomly seeded assertions that unequal values always have different hashes can fail through legitimate collisions. Use fixed seeds and selected vectors for discrimination tests, while testing equal-implies-equal-hash across many seeds. Use Roslyn semantic inspection for forbidden numeric conversions rather than a textual cast scan.

## Performance opportunities

### O1 — P2: Repeated header construction introduces quadratic generator work

**Evidence:** [Emitter.cs](src/DeepEquals.SourceGenerator/Emit/Emitter.cs), lines 117–216 and 259–271.

`HeaderValues(type)` runs for every closure type. Each call rebuilds the wrapper list by scanning all types, scans all roots, reconstructs capability/suppression text, and formats diagnostics. With T types, even the wrapper scan alone is O(T²). Many roots also produce O(T × roots) repeated header output. Writers/headers are constructed even for files later discarded because they contain no body.

**Action:** Compute context-invariant header values once, supply only type-specific values per file, and keep long root/diagnostic lists in the context file. Avoid opening known-empty files. Measure elapsed generation time, allocated bytes, and total generated bytes at 100, 1,000, and near 4,096 closure types, with one root and many roots.

### O2 — P2: Graph analysis repeatedly scans the whole graph and delays cancellation

**Evidence:** [ModelBuilder.cs](src/DeepEquals.SourceGenerator/Analysis/ModelBuilder.cs), `Propagate` (line 428), `SelectGuards` (line 490), and `HasUnguardedCycle` (line 515).

`Propagate` scans every edge until no flags change; an unfavourably ordered chain can require many passes. Guard selection calls `HasUnguardedCycle` once per candidate, allocating a `byte[n]` and scanning global node indices each time, even when the candidate's SCC is small. The loops do not check cancellation internally.

**Action:** Propagate flags with reverse-edge worklists or the SCC condensation graph. Store component-local node lists and reuse a stamped visitation buffer for guard trials. Add cancellation checks inside long loops. Benchmark long DAGs, many disjoint self-cycles, and one dense SCC; test cancellation during these passes, not just before and after model construction.

### O3 — P2: Stateless collision matching need not build every compatibility edge

**Evidence:** [DeepEqualsUnordered.cs](src/DeepEquals.SourceGeneration.Framework/DeepEqualsUnordered.cs), `MatchRun` (line 300).

Every collision run builds a full k-by-k compatibility matrix before finding a matching, including stateless element equality. That requires k² deep comparisons even when the first unused equal entry would settle each element. Stateful coinductive trials need the stronger matching algorithm because their answers depend on ancestor assumptions. For stateless equality satisfying the comparer equivalence contract, matching unused members by equivalence class can avoid the full matrix and workspace.

**Action:** Prototype a separate stateless path and preserve the existing stateful algorithm. Compare behaviour against the current implementation for duplicates and every enumeration order. Measure both comparison count and pool rentals for early matches, late mismatches, and expensive elements. Evaluate the Tree overloads only after their exception/depth contract is settled.

### O4 — P2: Exact-representation span equality can use bulk comparison

**Evidence:** [Emitter.cs](src/DeepEquals.SourceGenerator/Emit/Emitter.cs), `EmitSpanCores` (line 1755).

The span fast path calls `MemoryExtensions.SequenceEqual` only for default-compatible equatable leaves and explicitly excludes enums. Float/double spans therefore run scalar element loops that reinterpret each element's bits. Their required equality is exactly equality of the corresponding integer bit patterns, so a supported `MemoryMarshal.Cast<float, int>` or `Cast<double, long>` view followed by integer `SequenceEqual` can expose the runtime's bulk/vectorized comparison path without normalizing NaNs or signed zero. Enum spans can similarly compare their underlying representation.

**Action:** Gate this on the real span/marshal API surface and restrict it to proven representation rules. Do not apply it to custom/simple overrides or arbitrary structs with padding. Benchmark short and large spans with equal values and early/late mismatches; retain all NaN-payload and signed-zero tests. Keep hashing order unchanged.

## Additional scenarios and documentation gaps

**Stack safety needs an explicit boundary.** The README promises checks at every entry and recursive core, but [Emitter.cs](src/DeepEquals.SourceGenerator/Emit/Emitter.cs), lines 975–1005, emits checks only for stateful equality wrappers; retained-pair guards check again. Hashing and acyclic type chains do not. A finite type-graph bound is not a guarantee that the call chain fits the remaining thread stack. This is a source-confirmed coverage gap, not a reproduced process crash. Test long acyclic declaration chains and long guard-free paths within SCCs in a child process with a small stack. Reassess this when Tree makes hashing recursive; checking only selected SCC guards may leave a long path before the next check.

**Benchmarks should distinguish data shapes and outcomes.** Extend the existing scenarios with first/last-member mismatch, shared versus copied DAGs, 8/9 retained pairs, rollback after spill/growth, and collision runs around the cap. Measure cold initialization separately from warmed execution and include actual hash-table lookups, not only raw `GetHashCode` throughput. Tail hashing should include chain length and branch structure so a flattening optimization does not unnecessarily discard structure. No measured evidence in this audit establishes that XxHash64 is faster on every intended runtime.

**Existing documentation would overstate the work left to do.** The current implementation already has allocation-free decimal storage helpers, the dictionary default-key `TryGetValue` fast path, and partitioned generated files. README line 191 and Implementation §§6.9, 11–14 still contain contrary statements. `DeepEqualsState` now also rents a cached-hash array: the roughly 32 MB estimate omits about another 4 MiB at a million-pair rounded capacity on x64, before pool rounding/transient growth effects. Correct these descriptions alongside the new options so performance comparisons use the actual baseline.

**Plan sequencing:** Move option/model/naming plumbing and capability policy ahead of emitter changes that depend on them. Establish the depth-aware hash interfaces and Tree exception semantics before the unordered overloads. Do not use the new-interface marker as a substitute for an explicit supported generator/framework version matrix.

## Suggested order

1. Fix B1–B5 and retain the regression cases, checking CS8785 and nonempty output as well as compilation errors.
2. Amend the plan for P1–P3 before writing the Tree/64-bit emitter paths; add S1 as a design acceptance scenario.
3. Define fold normalization and deterministic representation tests, then implement the independent 64-bit runtime primitives.
4. Address O1/O2 while adding generator scale/cancellation measurements; evaluate O3 with comparison-count benchmarks.
5. Run all mode/width combinations through collection, dispatch, aliasing, boxed-cycle, checked-build, older-asset, and downstream smoke tests. Update the existing documentation claims as part of that work.
