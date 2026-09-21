# Future notes

What was learned building the generator: what was measured, what was tried and dropped and why, compiler and runtime
behaviour that shaped the code, and the problems still open. [Implementation](Implementation.md) states what the
generator emits; this file records why, and what to know before changing it.

- [1. Open problems](#1-open-problems)
- [2. Performance findings](#2-performance-findings)
- [3. Generated size](#3-generated-size)
- [4. Warnings in generated code](#4-warnings-in-generated-code)
- [5. Compiler, Roslyn and runtime behaviour](#5-compiler-roslyn-and-runtime-behaviour)
- [6. Packages](#6-packages)
- [7. Design decisions and their reasons](#7-design-decisions-and-their-reasons)
- [8. Testing and tooling](#8-testing-and-tooling)

**How the numbers were taken.** One Windows x64 machine, a 12th-generation Intel Core i9-12900HK, on .NET 10, .NET 8,
.NET Framework 4.7.2 and the Blazor WebAssembly interpreter. The downstream benchmarks run BenchmarkDotNet `ShortRun` in
process, where differences under about 10% are noise; the focused probes ran 4 warmups and 12 iterations in process.
Sub-nanosecond results sit at the harness's floor. The probes were scratch projects and are not in the repository.

## 1. Open problems

**Generation time.** Synthetic closures of sealed classes in one ring, each holding the next, a list of an earlier one
and a dictionary of another, on .NET 10 (`DeepEquals.Generator.Benchmarks`, BenchmarkDotNet `ShortRun` in process,
time and allocation per run, one root / every class a root):

| Declared classes | Closure files | Before | Now |
|---|---|---|---|
| 100 | 190 | 114 ms, 50 MB / 115 ms, 54 MB | 38 ms, 23 MB / 26 ms, 23 MB |
| 1,000 | 1,834 | 1.65 s, 499 MB / 1.76 s, 769 MB | 258 ms, 202 MB / 227 ms, 202 MB |
| 2,000 | 3,663 | 5.3 s, 1.1 GB / 6.1 s, 2.2 GB | 526 ms, 409 MB / 546 ms, 407 MB |

"Before" is the step 11 run, so the change also covers the header work of §3, which cut the text but not the
analysis. The suspicion had been the graph passes (`Propagate`, `SelectGuards`, `HasUnguardedCycle`). A sampling
profile (§8) showed otherwise: 63% of the time was `ClosureBuilder.ComputeDispatchCases`, almost all of it inside
Roslyn's conversion classifier, which `IsAssignable` called for every (candidate, case) pair of every dispatch type;
the 2,000-class closure has `object` as a dispatch type with 2,000 exact and about 700 assignable cases, so hundreds
of thousands of classifications, plus a `ToDisplayString()` per comparison inside the case sorts. Another 22% was
GC. What changed: `IsAssignable` now decides nearly every pair from the type hierarchy (base chain, implemented
interfaces, variance from the type arguments) and asks the classifier only for a nullable source, an array target or
a type argument it cannot settle, checked against the classifier over every pair of a representative type set
(`AssignabilityTests`); the case sorts compare a display name built once per type; `DeclaringShort` looks its type up
in a dictionary instead of scanning the closure per member; `HasUnguardedCycle` walks only its component's nodes and
stamps colours instead of allocating and clearing an array per call; and each file's header and skeleton are
written first into the one writer its body fills, so no body is copied. In a runner that reuses one compilation, the
2,000-class closure went from 1.95 s to 0.6 s per warm run.

A second pass with dotTrace's sampler (§8) found the rest: `GeneratedNames.All` built about fifty strings per type
twice per run, for the name reservation and the user-member check, through string interpolation (now one array per
short name, cached process-wide, since short names recur on every keystroke); `HasUnguardedCycle` walked the whole
component for each guard it tried to drop (now a walk from the dropped node alone, which suffices because the
unguarded nodes are acyclic with every candidate guarded, and the full walk stays as the fallback where that does
not hold); `EmitHeader` ran the template substitution once per file (now once per context); and per-member symbol
walks, the reference-assembly attribute, the constraint-only interface search and nameability, are cached per
assembly or per type for the run.

**Per keystroke.** The driver runs the attribute transform on every compilation, so each edit anywhere in the project
re-analysed every context. The analysis now keys on a fingerprint of the compilation's declarations, references and
options (`DeclarationFingerprint`; Implementation §12 states what it covers), and a compilation with the fingerprint
of the last one analysed gets the last model: an edit inside a method body costs one syntax tree's token walk plus the
emit skip the driver already had. Only a model without diagnostics is cached, since a diagnostic's position moves with
edits the fingerprint ignores. Edits that declare storage without a declaration, `field` in an accessor body and a
primary-constructor parameter used in a member body, are covered by keeping those bodies in the fingerprint;
`IncrementalTests` holds one case for each rule. Measured through one driver on the 2,000-class closure, as an editor
runs it: a run after a method-body edit in another file takes about 95 ms, the driver's own work, against about 600 ms
before; a run after a declaration edit in a file outside the closure takes 300 to 450 ms, the analysis alone, since the
model comes out equal and the driver keeps every output.

**A real project.** The synthetic closure is one ring of sealed classes; a real context looks different. Profiled on
an application's context of 60 roots that reaches 640 files (3.8 MB of generated text), through `MSBuildWorkspace`
and one driver: the closure walk was 72% of the analysis, with `ReadableThroughGetter` alone 22%, because
`Compilation.IsSymbolAccessibleWithin` scans every reference of the compilation on each call and was asked twice per
property; the compiler bound the attributes of every member on every run, since `GetAttributes` binds a source
symbol's attributes on first request per compilation; `BuiltInLeaves.Find` built a display string per type; and
`CapabilityProbe.Find` searched every reference for types the core library has. Now a public symbol whose containing
types are public is accessible without asking (`SymbolAccess.IsAccessibleWithin`); a source symbol whose declaration
carries no attribute list answers `Attributes` without binding; an attribute is compared by its short name before its
full one; metadata names are cached on the symbol, which for a metadata symbol outlives the compilation; the core
library is probed first; and the closure builder's assignability questions go through `AssignabilityCache`, which
refuses a pair at once when the target's definition is not among the source's bases and interfaces. Per keystroke on
that project: a method-body edit 16 ms, a declaration edit outside the closure 140 to 156 ms (from 300 to 370), a cold
run 0.7 s.

What remains there is mostly the compiler's: the analysis runs on the `MetadataImportOptions.All` view, a second
compilation whose symbols, attribute bags and interface lists are built again on every keystroke; `GetDeclaredSymbol`
on it alone is about 25 ms per run. Working on the host's own compilation and consulting the All view only for the
private members of types from metadata references would remove that, at the cost of two symbol universes in one
analysis.

What remains on the synthetic closure, from the profile after these changes: GC (about 20%); the emitter and `ModelBuilder` (about 20% each);
`ClosureBuilder.Expand` and `SelectMembers`, which are symbol walks (8%); `IsAssignable` itself, now mostly
`AllInterfaces` scans (about 15%); and Roslyn's `AddSource`, which checks each hint name against every earlier one,
quadratic in the number of files, about 45% of the output stage for 3,663 files. That share is the benchmark's own
doing: its names are all the same length, and `OrdinalIgnoreCase` compares lengths before characters, so every pair
is compared in full (45 ms for 3,663 such names, 1.6 ms for 640); the 640 names of a real context, whose lengths
vary, cost 0.3 ms. Nothing but fewer files would change it, and fewer files would cost more than they save (§5).
Cancellation is checked inside the long loops: a run cancelled 20 ms in returns after about 51 ms. 4,000
classes pass the 4,096-type closure cap and time a `DEQ018` refusal instead, so the benchmark stops at 2,000 and its
setup throws on any generator error.

**Unordered matching degrades on fingerprint collisions.** A set of 100 keys with all 100 sharing a fingerprint compares
in 10.8 µs against 0.21 µs with none: colliding elements fall to a k-by-k matching matrix of deep comparisons. A
stateless matching pass over that matrix is the candidate fix; the fingerprint benchmarks measure it.

**`Path` is exponential on heavily shared graphs.** A diamond with eight levels of sharing compares in 0.42 µs under
`Graph`, 58 µs under `Path` and 15 µs under `Tree`: `Path` and `Tree` re-compare a shared subgraph once per path to it.
Documented; such data belongs under `Graph`.

**Object dispatch costs about 3.5 ns per value over a virtual `Equals`** on .NET 10 and .NET 8, which is Payload's
1.5; the state, the closure size and the cycle mode are not involved (§2.7). **.NET Framework 4.7.2 trails the
hand-written code** on Customer and list equality by about 20% and on Payload by 70% (§2.7).

**Remaining size.** See §3 for what still costs bytes and the options not taken.

## 2. Performance findings

### 2.1 A 64-bit hash stream was slower, and was removed

**What was built.** An option `Hashing = XxHash64` ran every generated hash core over xxHash64 with 64-bit words and its
own per-process seed, folding only the public `GetHashCode` to 32 bits (never to 0, so an empty collection did not hash
like null). A 64-bit leaf was one word and a 128-bit leaf two; nested hashes and unordered sums carried 64 bits; two
32-bit values packed into one word, narrow words paired before wide ones. The hypothesis: half the words, so about half
the rounds, each round one instruction on 64-bit processors and in WebAssembly.

**Result.** Public `GetHashCode`, XxHash32 / XxHash64, scenario harness:

| Scenario | .NET 10 | .NET 8 | .NET Framework | browser |
|---|---|---|---|---|
| Customer | 23.9 / 26.4 ns | 28.0 / 30.9 ns | 44.4 / 40.2 ns | 243 / 245 ns |
| Order | 373 / 527 ns | 448 / 568 ns | 1,363 / 1,187 ns | 6,165 / 5,891 ns |
| `IReadOnlyList<OrderLine>` x100 | 1,335 / 1,246 ns | 1,537 / 1,517 ns | 3,180 / 2,722 ns | 14,680 / 14,535 ns |
| `Dictionary<string, decimal>` x100 | 733 / 1,716 ns | 837 / 1,948 ns | 3,117 / 3,067 ns | 16,509 / 12,721 ns |
| `record struct Money` | 12.9 / 54.2 ns (noisy) | 5.8 / 19.0 ns | 19.3 / 27.8 ns | 142 / 122 ns |
| `record struct Point3` | 3.2 / 6.9 ns | 4.8 / 7.1 ns | 5.7 / 7.4 ns | 94 / 109 ns |
| `double[]` x1000 (bit block) | 174 / 162 ns | 298 / 292 ns | 1,148 / 1,125 ns | 2,747 / 2,753 ns |

Slower on .NET 10 and .NET 8 almost everywhere, faster only on .NET Framework for a few scenarios and in the browser for
the decimal-heavy ones, level on bit-block sequences, which hash through XxHash3 at either width.

**Why.**

- An xxHash64 tail round costs three multiplies where xxHash32's costs two, and a 64-bit multiply is no cheaper on x64.
- Packing pushes most objects under four words, where both hashes run serial tail rounds instead of four parallel
  lanes: Point3 is six words and uses lanes under xxHash32, three serial rounds under xxHash64.
- Where xxHash64 does use lanes it merges them with four extra rounds xxHash32 has no counterpart for.
- A wide leaf outside a member stream was a nested hash with its own finalization.
- A store-forwarding stall on decimals, the largest single cost (§2.2 has the mechanism). Money's hash on .NET 8:

| Shape | XxHash32 | XxHash64 | XxHash64, words from two 32-bit reads |
|---|---|---|---|
| public `GetHashCode`, decimal as a nested hash | 26.3 ns | 64.7 ns | 29.0 ns |
| member stream, decimal as words | 24.9 ns | 33.2 ns | 24.7 ns |
| a decimal alone | 7.2 ns | 6.4 ns | |
| three doubles | 11.3 ns | 11.4 ns | |

**Wide objects**, with the decimal reads patched as in the last column above:

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
decimal reads still stalled, because the JIT copied the decimal with one 16-byte vector store and 4-byte reads from it
do not forward.

**Decision.** Removed: a few nanoseconds on numeric-heavy types did not pay for a second code path through every hash
core. Do not revisit without a workload dominated by 64-bit fields. Vectorizing the xxHash32 lanes was not attempted:
the four scalar lanes already overlap in the CPU, and the 32-bit vector multiply is slow on Intel.

### 2.2 Equality against a record's own `Equals`

The target is to be at least as fast as a record's compiler-generated `Equals`.

**How the runtime does it**, from the .NET 10 disassembly of `record struct Money(decimal Amount, string Currency)`: the
record's `Equals` inlines into its caller; `EqualityComparer<decimal>.Default` devirtualizes to `decimal.Equals`, which
reads the backing field's `_flags`, `_hi32` and `_lo64` at their own widths and calls `VarDecCmpSub` only for two nonzero
values of the same sign; the string compare inlines to reference, null and length checks and one `SequenceEqual`.

**The decimal compare was not the problem.** Compares read through the record struct's property, .NET 10:

| Compare | Time |
|---|---|
| `DecimalEquals`, two 64-bit reads | 1.00 ns |
| four 32-bit reads | 1.03 ns |
| `MemoryMarshal.AsBytes(...).SequenceEqual` | 0.75 ns |
| `Vector128` equality | 0.96 ns |
| `decimal.Equals`, numeric | 2.59 ns |
| the record's whole `Equals` | 2.20 ns |
| `DecimalEquals` on a class field, in place | 0.05 ns |

**The copy around it was.** A getter returns the decimal by value. The JIT inlined the comparer and kept both Money
values in registers, then, because `DecimalEquals` takes the decimal's address, stored the fields back to the stack as
32- and 64-bit pieces and read the first eight bytes as one 64-bit load spanning two 32-bit stores: a store-forwarding
stall per operand. The record spills too but reads each field back at the width it stored.

| Shape | .NET 10 | .NET 8 |
|---|---|---|
| record `Equals` | 2.00 ns | 2.55 ns |
| getter, two 64-bit reads | 3.53 ns | 4.65 ns |
| getter, the runtime's field widths (int, int, long) | 0.61 ns | 4.95 ns |
| getter, `Unsafe.BitCast` to `UInt128` | 3.31 ns | 3.12 ns |
| in place, two 64-bit reads | 0.44 ns | 0.34 ns |
| in place, field widths | 0.82 ns | 0.64 ns |
| inlined, getter, field widths | 0.61 ns | 0.64 ns |
| inlined, in place, two 64-bit reads | 0.67 ns | 0.35 ns |
| record through `IEqualityComparer<T>` | 3.26 ns | 2.64 ns |
| in place, field widths, through `IEqualityComparer<T>` | 1.57 ns | 1.44 ns |

Field widths win only where the JIT keeps the fields in registers (.NET 10); on .NET 8 the copy is one vector store and
the same reads stall. Reading in place is fast on both, whatever the compare. Hence the rules the emitter follows: an
auto-property of a value type wider than a register is read in place through `[UnsafeAccessor]` where available; a
nullable's struct or decimal payload is reached by reference through `Nullable.GetValueRefOrDefaultRef`; a stateless
struct comparer's `Equals` and `GetHashCode` are `AggressiveInlining` (a stateful one holds a `try`/`finally`, which the
JIT does not inline).

**Result**, record → generated in nanoseconds, direct then through the interface, with distinct string instances:

| Model | .NET 10 | .NET 8 |
|---|---|---|
| Money | 3.85 → 1.93; 4.61 → 2.90 | 4.40 → 3.09; 5.07 → 3.68 |
| Invoice | 11.1 → 9.0; 11.7 → 9.5 | 14.4 → 14.9; 15.4 → 14.8 |
| Person | 11.5 → 9.6; 12.3 → 9.6 | 14.2 → 13.9; 15.3 → 13.8 |
| Point3 | 0.37 → 0.27 | 0.36 → 0.52 |

Person was slower until struct members were read in place: through getters each `x.Position.X` copied the whole
`Point3` and each `x.Balance` the whole `Money?`. Point3 compiles to the same six loads and three compares on both
sides and moves below the harness's resolution from run to run.

**Tried and reverted:** reading `float` and `double` auto-properties in place too. The JIT emitted the identical load,
and on .NET 8 Point3 lost its use-site inlining (1.38 ns against 0.30).

### 2.3 Source form does not change the machine code

Asked whether a conditional expression (`c ? a : b`) is faster than `if`/`else`, and whether folding `Equals_T`'s
null-check prelude into one expression would help. Compiled with full optimization on .NET 10 and compared:

| Pair | Instructions | Bytes | Branches |
|---|---|---|---|
| `b ? a : c` against `if (b) return a; return c;` | 4 / 4 | 9 / 9 | 0 / 0, both one `cmov` |
| `o.HasValue ? f(o) : 0` against the `if` form | same | 30 / 30 | 1 / 1, neither uses `cmov` |
| reference prelude then members, members inlined | 40 / 40 | 119 / 119 | 11 / 11 |
| reference prelude then members, members called | 17 / 17 | 48 / 48 | 3 / 3 |

The JIT builds the same flow graph from either form and decides on a conditional move from that graph: only when both
arms are cheap and free of side effects, which a call to the member chain never is. The only difference is which path
falls through without a jump: the `if` form falls through to the member comparison, the common case of two distinct
non-null objects; the folded form takes two jumps to reach it. That favours the `if` form, by less than can be
measured once dynamic PGO reorders the blocks. Folding would also save only 0.1% of generated size (§3), so it was not
done.

### 2.4 Cycle handling

.NET 10, the downstream `TreeNode` (585 nodes, branching 8, depth 3): equality 10.8 µs under `Graph`, 5.1 µs under
`Path`, 2.2 µs under `Tree`, against 1.8 µs for the built-in `Equals`, which does not track cycles. `Tree`'s full hash
costs 3.4 µs where `Graph`'s one-level hash costs 26 ns. A 100,000-node chain compares in 2.8 ms, 3.3 ms and 0.16 ms. The
ninth retained pair spills `Graph`'s inline table and adds about 300 ns; `Path` holds only ancestors and stays inline.
The diamond numbers are in §1.

### 2.5 Fingerprints and unordered matching

The audit's S1 shape, 65 chains `0 -> 0 -> i -> null` in two equal sets, all with the same public hash, compares in
86 µs at `MatchingHashDepth = 1` with a wide collision cap, 5.3 µs at 2 and 16 µs at 4 (noisy). At depth 1 with the
default cap of 64 it throws `DeepEqualsComplexityException`: the default of 4 exists for this shape.

### 2.6 Bit blocks

Sequences of bit-block elements compare as bytes (`SequenceEqual`) and hash through XxHash3, on every runtime,
including the browser interpreter, where it was in doubt because XxHash3 emulates its 64x64-to-128-bit multiply there:
`double[]` and `Point3[]` of 1,000 compare in 1 to 2% of the built-in time in the browser and hash in 4 to 8%. The byte
path therefore stays on for WebAssembly.

### 2.7 Emitter changes measured on 16 and 17 September 2026

**How.** The scenario harness, `ShortRun` in process, on an idle machine overnight (17 September, 02:00 to 05:00): the
full equality suite twice per package on .NET 10 and .NET 8, targeted runs on .NET Framework 4.7.2, and a scratch probe
project. Every number is the generated-over-built-in ratio inside one run, and a change counts only where both repeats
and both runtimes agree. A daytime attempt on 16 September, with the machine in use, drifted 20 to 40% between launches
on the built-in side alone and is not quoted. Packages `1.0.0-alpha260916100` (baseline) to `107` (final) are in the
local feed until the smoke suite needs it emptied (§8); the raw reports are under `D:\Temp\deepequals\night`.

**What was built**, one package per step:

- **A. Boxed leaves unboxed in place** (101). A dispatch core compared a boxed decimal as `DecimalEquals((decimal)x,
  (decimal)y)`, two copies out of the boxes; the case took `Unsafe.Unbox<decimal>(x)` instead. **No effect; reverted.**
- **C. `string` first among the hoisted dispatch tests** (102). The hoisted sealed cases kept the assignable order, which
  sorts by display name, so `System.TimeZoneInfo` and `System.Version` were tested before `string`. **No effect; kept**,
  since it costs nothing and matches the comment that claimed it.
- **B. Wide leaves flattened into entry hashes** (103). A dictionary value or set element of a wide leaf hashed as a
  nested `Hash(decimal)`, four words with their own finalization, inside `Combine(key, value)`; the entry stream takes
  the four words directly (`EntryWords`), as a member stream already did. **Kept.**
- **D. Wide leaf words read in place** (104). A member stream copied a decimal, Guid or `DateTimeOffset` reached
  through a field or `[UnsafeAccessor]` into a local before splitting it into words; the words are read through the
  reference instead (`WideLeafWords`, `reference`). **Kept for the .NET 8 asset only** (`WordsInPlace`).
- **E. The dispatch map keyed by type handle** (105). Above `MaxSwitchCases` a dispatch core looks its runtime type up
  in a `Dictionary<Type, int>`; it is now a `Dictionary<IntPtr, int>` over `TypeHandle.Value`, so the lookup hashes and
  compares a pointer instead of calling `Type.GetHashCode` and `Type.Equals`. **Kept.**

**Result**, baseline against A to D together, both repeats:

| Scenario | .NET 10 | .NET 8 |
|---|---|---|
| Payload (object dispatch) Equals | 1.60, 1.68 → 1.51, 1.53 | 1.30, 1.37 → 1.38, 1.45 |
| Payload hash | 1.50, 1.56 → 1.59, 1.53 | 1.16, 1.26 → 1.15, 1.19 |
| Customer hash | 1.26, 1.28 → 1.25, 1.24 | 1.26, 1.24 → 1.27, 1.25 |
| `Dictionary<string, decimal>` x100 hash | 1.37, 1.39 → 1.25, 1.20 (812 → 746, 797 → 701 ns) | 1.32, 1.36 → 1.18, 1.26 (821 → 745, 824 → 767 ns) |
| `Dictionary<SkuId, int>` x100 hash | 2.06, 1.98 → 2.07, 2.01 | 2.08, 2.08 → 2.06, 2.07 |
| `IReadOnlyList<OrderLine>` x100 hash | 1.36, 1.38 → 1.35, 1.38 | 1.38, 1.40 → 1.37, 1.43 |
| Order hash | 1.47, 1.49 → 1.48, 1.47 | 1.39, 1.42 → 1.34, 1.34 |
| `record struct Money` hash | 1.65, 1.90 → 2.21, 2.12 (4.9 → 5.6, 4.8 → 5.4 ns) | 2.89, 2.81 → 1.53, 1.39 (11.9 → 6.3, 10.9 → 5.6 ns) |
| `record struct Point3` hash | 2.23, 2.58 → 2.39, 2.45 | 2.48, 2.57 → 2.48, 2.59 |
| everything else | within 0.05 | within 0.05 |

Isolation runs on .NET 8: 103 (A, B and C without D) leaves Money's hash at 11.5 ns, so the halving is D alone; 101
(A alone) leaves Payload at 1.36. On .NET 10, D costs Money 0.6 ns in both repeats, and a variant that took one
`ref readonly` local instead of four accessor calls (106) cost 1.1 ns: there the copy is register-promoted and the
word reads out of it are free, while on .NET 8 the copy is one vector store from which the four-byte reads do not
forward (§2.1, §2.2). Customer's decimal, a class field, moved on neither runtime. Hence the gate: in place only on the
asset that has `[UnsafeAccessor]` but not its generic form, which is .NET 8 exactly.

**B showed nothing on 16 September and 8 to 14% overnight**: the string-keyed dictionary hashes an entry in about 8 ns
and the nested finalization overlaps with the next entry's stream, so the saving is a fraction of a round per entry,
visible only on a quiet machine.

**Where Payload's 1.5 comes from.** A probe project with four contexts over the same `Payload` (a `Dictionary<string,
object>` of eight values of eight types, plus one `object`), nanoseconds, .NET 10 / .NET 8:

| Comparison | .NET 10 | .NET 8 |
|---|---|---|
| hand-written (virtual `Equals` per value) | 81 | 139 |
| generated, Graph, the downstream context | 115 | 178 |
| generated, Graph, a context of `Payload` alone | 110 | 185 |
| generated, Path | 120 | 177 |
| generated, Tree (no state) | 111 | 171 |
| the dictionary alone, generated / hand loop | 116 / 78 | 235 / 129 |

The state costs nothing: Graph, Path and Tree agree, and the closure size does not matter. The gap is per dispatched
value, about 3.5 ns over a virtual `Equals` on both runtimes. A one-entry dictionary per value type (below) puts it in
the chain plus the call: 0.3 to 0.4 ns per `is` test on .NET 10 and 1.5 ns on .NET 8, from `int` (fourth test) to
`DateTime` (tenth), and the map path for a type that is not a hot leaf (`SkuId`) at 7 ns on .NET 10 and 24 ns on
.NET 8 over the hand loop before E, 4 and 22 after it. The dispatch core is at Tier1 with PGO
(`DOTNET_JitDisasmSummary`: IL 4,147 bytes, 8 KB of code), so it is not an unoptimized method. One-entry dictionary,
generated under Tree / hand loop:

| Value | .NET 10 | .NET 8 |
|---|---|---|
| `int` | 11.1 / 6.8 | 31.7 / 15.8 |
| `long` | 9.6 / 5.1 | 28.3 / 13.5 |
| `string` | 12.0 / 7.2 | 31.7 / 16.4 |
| `decimal` | 11.9 / 8.9 | 33.3 / 18.4 |
| `DateTime` | 12.8 / 6.2 | 40.9 / 15.8 |
| `Guid` | 12.9 / 6.2 | 39.2 / 15.5 |
| `SkuId`, through the map | 21.5 / 6.4 | 55.7 / 16.8 |
| `SkuId`, map keyed by handle (E) | 17.7 / 6.3 | 53.2 / 16.3 |

On .NET 8 the one-entry call carries about 12 ns more fixed cost than on .NET 10, which the eight-entry Payload does
not show per value; the `Comparer` checks of the string-keyed fast path, which on .NET 8 unwrap the dictionary's
internal non-randomized comparer through a virtual call per side, are the candidate, unmeasured. E on the Payload
scenario itself: 182 → 165 ns on .NET 8 (ratio 1.45 → 1.27), unresolved on .NET 10, where the built-in side moved 30%
between the two runs.

**F. The closure's own cases chained ahead of the map** (109, from the same numbers). The chain costs less than the
map until about a dozen tests on either runtime, and a user type behind `object` is likelier than
`System.Drawing.Size`, so above `MaxSwitchCases` the chain now holds the hot leaves and then up to `MaxSwitchCases` of
the closure's own exact cases, value types first (an id, an enum, a money struct is what a payload boxes most often),
and only the rest go through the map (`DispatchCases.Chained`, `OwnCases`). In the probe's context the `SkuId` value
went from the map to the twelfth test: 21.5 → 15.0 ns under Tree and 32 → 18 under Graph on .NET 10. The Payload
scenario was measured only with the machine in use afterwards and could not resolve it. The cost side: a cold BCL
leaf now passes up to twelve more tests before the map, about 4 ns on .NET 10 and 18 on .NET 8.

**.NET Framework 4.7.2** (one run each, unchanged by A to D) has gaps of its own that .NET Core does not: Customer
Equals 1.24, `IReadOnlyList<OrderLine>` Equals 1.22, Payload Equals 1.72 and hash 1.68, Money hash 1.94,
`Dictionary<SkuId, int>` hash 1.58, while `Dictionary<string, decimal>` Equals is 0.78 and Customer's hash 0.66. The
probe on net472 (morning of 17 September, one run) places Payload's gap in the dispatch core again, at about 13 ns
per value over a virtual `Equals` (a hand loop calling the generated `object` comparer per value: 299 ns; the generated
dictionary core: 282; the hand loop with virtual `Equals`: 196), but not in the type tests, which cost 0.3 to 0.5 ns
each there as on Core, nor in the map, 9.6 ns by `Type`. What the 60-case core spends the rest on is unattributed; a
hand-written chain of a few cases against the generated core would say whether the Framework JIT handles the method's
size badly. E is a loss there, 12.6 ns against 9.6: `IntPtr` has no `IEquatable<IntPtr>` on .NET Framework, so the
default comparer boxes, and `TypeHandle.Value` is a 3.6 ns call; hence `MapByHandle` keys by handle only on .NET 8
and later. A Graph state costs about 24 ns per top-level call on net472 against 3 on Core, since the netstandard2.0
asset has no `InlineArray` and zeroes eight explicit pair fields. The Customer and list equality gaps are unmeasured;
the getter copies of decimal, Guid and `DateTime` members through `in` parameters, the §2.2 stall on an older JIT with
no `[UnsafeAccessor]` to avoid it, are the candidate, and a `ref`-returning `DynamicMethod` accessor (`ldflda`) would be
the netstandard2.0 counterpart to test. Money's hash and the `SkuId` dictionary's are the seeded Marvin and the seeded
stream against the runtime's native, non-randomized string hash and two multiply-adds: floors, by the decision to keep
the seed.

**Floors, not defects of these changes.** `Dictionary<SkuId, int>`'s hash at 2.0 and Point3's at 2.5 are the seeded
stream against the record's two multiply-adds. An unordered hash sums per-entry hashes, and without a finalization per
entry the sum is linear in the entries, so two dictionaries that swap values between keys collide whatever the seed;
the per-entry finalization stays. Trees under Graph at 6.0 (12.1 µs against 2.0), Path at 3.4 and Tree at 1.2 are
§2.4; the chain suite puts Graph at 19 ns per node (10 nodes: 190 ns against Tree's 13), and a 100,000-node chain at
2.9 ms Graph, 3.3 ms Path, 0.14 ms Tree. Bit blocks are unchanged: 16 doubles compare in 2.0 ns against 10.7 and hash
in 7.1 against 11.3; the same 16 doubles hashed through an `IReadOnlyList<double>` view cost 42 ns, six times the array.

**Tier0 frame size bounds recursion.** The first form of A unboxed every value leaf in place, including the rules that
take the value (`==` on `Guid`, `.Equals` on the drawing structs, the component reads of a `Matrix4x4`). It failed
`Tree_boxed_struct_cycle_through_object_throws`: a `Cell -> object -> Cell` chain 512 deep hit
`InsufficientExecutionStackException` before the depth guard. The test runs the cores a handful of times, so they are
at Tier0 (`DOTNET_JitDisasm` shows `MinOpts`), where every by-value copy made from a reference and every byref
temporary gets its own frame slot: the `object` dispatch core's frame went from 0x880 to 0xCA0 bytes with all leaves
unboxed in place, and 0x8A0 with decimal and `DateTimeOffset` alone. A dispatch core over every BCL leaf is on the
recursion path of any cyclic type behind `object`, so its Tier0 frame is a budget, not a detail.

**The final form** (107: B, C, D gated to .NET 8, E; A reverted), one full run against both baseline repeats:

| Scenario | .NET 10 baseline → final | .NET 8 baseline → final |
|---|---|---|
| Payload Equals | 1.60, 1.68 → 1.57 | 1.30, 1.37 → 1.33 |
| Payload hash | 1.50, 1.56 → 1.47 | 1.16, 1.26 → 1.17 |
| `Dictionary<string, decimal>` x100 hash | 1.37, 1.39 → 1.28 | 1.32, 1.36 → 1.21 |
| `record struct Money` hash | 1.65, 1.90 → 1.92 (4.9, 4.8 → 4.8 ns) | 2.89, 2.81 → 1.34 (11.9, 10.9 → 5.4 ns) |
| Order hash | 1.47, 1.49 → 1.45 | 1.39, 1.42 → 1.34 |
| everything else | within the repeats' own spread | within the repeats' own spread |

What the night settles: the four ideas of 16 September were worth a tenth of a nanosecond each on .NET 10, the .NET 8
decimal copy was the one real defect and is fixed where it exists, and the ratios still above one are the object
dispatch chain (§1, now shorter for the closure's own types through E and F), the seeded hash against a record's
polynomial, and the cycle guards, none of which these changes touch.

## 3. Generated size

**Measured** over the downstream models (144 files, C# 14) and the scale closures of §1:

| Sample | Size |
|---|---|
| scale closure, 2,000 classes, every class a root, when every type file's header repeated the registered roots | 146 MB |
| the same once the context description moved to the context file alone | 25.3 MB, one root or every root |
| the same with framework aliases, a one-line notice in type files and file-scoped namespaces | 19.5 MB |
| 1,000 and 100 classes, now | 9.7 MB and 1.01 MB |
| downstream models, before and after the aliases, notice and namespace changes | 1,079,928 → 864,273 characters (−20.0%) |

Output no longer depends on the number of roots.

**Where the bytes go now**, downstream models: `global::` prefixes 25.0% (`System.Collections.Generic.` alone was 5.2%
before the aliases, and user type names about as much), indentation 13.9%, XML documentation 4.9%, headers 2.7%,
`[GeneratedCode]` lines 1.5%, `[MethodImpl(AggressiveInlining)]` lines 0.8%.

**Options measured and not taken:**

- Two-space indentation: 7.9% smaller. Declined; it does not read as C#.
- Folding the null-check prelude of `Equals_T` into one expression: 0.1%. Only 13 of 111 preludes are a bare prelude
  followed by a return; the rest carry cycle guards or dispatch. No speed difference either (§2.3).
- Merging `EqualsMembers_T` into `Equals_T` where only one caller exists: 0.3% (22 methods in the sample).
- `_DeqHelpers.FloatBits` appears 1,200 times in the sample, twice per float compared, mostly from the `System.Numerics`
  types; a one-call float compare would halve that, at the cost of framework code.
- XML documentation cannot go while generated code must raise no CS1591 (§4).

**Aliases, and why only these.** Only non-generic classes can take a using alias, so the generic ops interfaces and
`FieldGetter` stay `global::`-qualified. A namespace alias used with `::` (`Deq::DeepEqualsHelpers`) would never clash
with a type, but saves less per use. An alias at the top of the file would clash with a `global using` alias of the
same name (CS1537), which is why the aliases sit inside the namespace declaration; a context in the global namespace
has no such scope and declares them at the top. Inside the namespace an alias still clashes (CS0576) with a type of
the same name declared in that namespace, so the names start with an underscore (`_DeqHelpers`), which no type name
follows by convention: a clash takes a type named that on purpose. No diagnostic reports it.

## 4. Warnings in generated code

Generated code raises no warning of its own under every warning wave, documentation diagnostics and full nullable
analysis; the generator tests compile every generated file that way and fail on any warning. Getting there:

- CS1591: every public member carries XML documentation. A doc comment must precede the attributes, or CS1587.
- CS8981: a type named in lower case is redeclared verbatim, `@c`.
- CS9192 and CS9193: `Nullable.GetValueRefOrDefaultRef` takes `ref readonly` from .NET 8. Neither warning is a
  correctness problem (CS9192 is call-site style; under CS9193 the compiler copies the value to a hidden temporary used
  in the same expression). The emitter passes `in` on a variable and calls `GetValueOrDefault()` on a value instead.
- CS8620: the only warning full nullable analysis raised, 55 times. One private core serves every nullability spelling
  of a type, since `List<string>` and `List<string?>` are one runtime type, and C# checks nested nullability at every
  call. A cast turns it into CS8619. The private implementation is therefore declared under `#nullable disable
  annotations`, where an oblivious parameter accepts every spelling and bodies keep flow analysis.
- CS0108, CS0162, CS0168, CS0219, CS8019 and CS8632, once suppressed by default, never fired.

**What cannot be avoided**, and is suppressed, one pragma line each naming the symbol, only in files that need it:

- An `[Obsolete]` type in a name generated code must write. The private cores must name it, and an obsolete context
  silences uses only inside itself: marking the cores `[Obsolete]` moves the warning to their callers, the comparers of
  containing types that are not obsolete. (A type obsolete as an error compiles only inside an `[Obsolete]` context,
  which silences even error-level uses; elsewhere it is `DEQ038`.)
- An `[Obsolete]` member where no `[UnsafeAccessor]` exists (before .NET 8, and for a generic declaring type on
  .NET 8): reading it means naming it. Where an accessor exists the member's storage is read and nothing is suppressed.
- `[Experimental]` and preview-feature ids. An experimental context silences uses only inside its own body, not its
  callers; verified.

## 5. Compiler, Roslyn and runtime behaviour

- **`Compilation.ClassifyConversion` costs about a thousand times a hierarchy walk**: it classifies user-defined,
  numeric, tuple and variance conversions before answering, and the generator only ever wanted identity, reference
  and boxing. Ask it last, not first (§1).
- **`SourceProductionContext.AddSource` compares each hint name with every earlier one**, case-insensitively
  (`AdditionalSourcesCollection.Contains` is a linear scan, and the driver merges every output through it again in
  `AppendOutputs`): the cost of a context is quadratic in its number of files, whether they come from one
  `RegisterSourceOutput` call or many, and System.Text.Json's generator, one file per type in the same shape, pays it
  too. It is small unless names share a length (§1). The driver parses generated text lazily (`ParseTextLazy`) and
  never reuses a tree between runs; the IDE workspace does, hashing each file's text and keeping the document, and its
  parsed tree, when the hash is unchanged (`SourceGeneratedDocumentState.WithText`). One file per type is what lets
  that work: a declaration edit re-parses the files it changed, not the context.
- **`IList<T>.CopyTo` beats an indexer walk even for a short list** (20 doubles: 20 ns against 56 ns through a
  stack copy by indexer), so `DeepEqualsBlocks.HashList` copies through the pool while `HashReadOnlyList`, whose
  interface has no `CopyTo`, walks the indexer and keeps a stack path for short lists.
- **Roslyn folds a decimal constant converted straight to a span** in `new[] { 1.50m }`, losing its scale. Tests that
  need a decimal's scale go through locals.
- **Roslyn exposes no layout for metadata structs** and drops `[StructLayout]` from `GetAttributes()`. Bit-block
  structs are therefore source-declared only, with layout read from syntax, and each is checked at run time against
  `Unsafe.SizeOf<T>()` so a layout surprise costs speed, never correctness.
- **Older System.Collections.Immutable releases lack `ImmutableArray<T>.AsSpan()`**; pinning System.Memory on
  netstandard2.0 exposed it, hence the `HasImmutableArrayAsSpan` probe.
- **Generic `[UnsafeAccessor]` is missing on .NET 8** for a generic declaring type, which falls back to reflection-built
  delegates; newer runtimes take the accessor.
- **`BitConverter` bit reads and `Unsafe.As` over a `ref` are JIT intrinsics**, one register move, and neither spills
  the local. The raw pointer form `*(uint*)&f` is slowest: taking the address spills the local for the rest of the
  method. `Unsafe.BitCast` adds nothing over `BitConverter` for 4- and 8-byte values and is not in the out-of-band
  `Unsafe` package.
- **A `using` alias inside a namespace declaration** shadows a same-named type of an enclosing namespace and a
  compilation-unit or `global using` alias; it clashes (CS0576) only with a type of that name in the same namespace.
- **The hint name comparison is case-insensitive** in Roslyn, across the compilation.

## 6. Packages

- **System.IO.Hashing is chosen per framework asset**: the newest release that builds without a warning on every runtime
  that asset serves. 10.0.12 on net10.0, net8.0 and netstandard2.0 (.NET Framework 4.6.2 and later); 8.0.0 on net6.0,
  because 9.x and 10.x warn on net6.0 and net7.0.
- **The netstandard2.1 asset has no block helpers**: every System.IO.Hashing release warns on the netcoreapp3.1 to
  net5.0 runtimes it serves. The generator probes `HasFrameworkBlocks`, a target-framework capability.
- **System.Memory 4.6.3 is pinned on netstandard2.0**, so .NET Framework consumers get `ReadOnlySpan<T>` and the span
  paths.
- Left alone on purpose: FluentAssertions 8 (licence change) and xunit.runner.visualstudio 4 (targets xunit v3).

## 7. Design decisions and their reasons

- **The framework ships as little as possible.** Generated code calls BCL methods directly
  (`Nullable.GetValueRefOrDefaultRef`, `Unsafe.AsRef(in x)`, `BitConverter.HalfToUInt16Bits`) rather than framework
  wrappers. What remains in `DeepEqualsHelpers` has no BCL equivalent: per-asset float and double reads, decimal, Guid
  and DateTime storage, the constrained `EquatableEquals`, and the throw helpers.
- **Options are per context only.** A per-root override would need two variants of every shared core.
- **Dropped from the start:** a stable hash mode and a declared-type polymorphism mode. Users seal classes for dispatch
  performance, and the README says so.
- **Same-version packages.** The generator and framework ship at one version; a mismatch is `DEQ036` and otherwise
  unsupported. Capability probes are a different axis, target framework rather than package version.
- **`Tree` bounds its traversal and does not validate inputs.** `Equals(a, a)` is true even when `a` closes a cycle;
  two roots sharing a cyclic child are equal when the comparison reaches the shared reference; an unequal member before
  the cycle is false; only a cycle the traversal enters throws, naming the type whose guard ran out of depth.
- **`MaxDepth` defaults to 512.** System.Text.Json stops reading at 64. Linked lists do not count, because the tail loop
  keeps depth flat. The stack check stays in the guard, so a small thread stack degrades to an exception, not a crash.
- **Brent's detector runs after each node is compared**, so a finite chain that differs first returns false instead of
  throwing. One side is enough: if either chain is finite the loop ends at its null.
- **`Path` rolls back through `try`/`finally` in every guarded core** rather than splitting each core into a guard and a
  body: one shape everywhere, and exception-safe.
- **Hash values differ between modes** (`Tree` hashes the whole tree); hash values are already per process.
- **The matching fingerprint is not the public hash.** It is never observed, so it can look `MatchingHashDepth` payload
  edges into a cycle where the public hash looks one. A level-`k` hash depends only on the `k`-bounded unrolling, so a
  rolled cycle and its unrolling still fingerprint alike. Cost is linear in `k` for chains and `b^k` for branching `b`.
- **Raw bits, never a numeric conversion.** A conversion rounds, saturates, collapses NaN payloads and merges `-0.0`
  with `0.0`. Only bit-preserving integer casts appear in hash words, always inside `unchecked`, because consumers may
  compile with `CheckForOverflowUnderflow`.
- **Not bit blocks:** `bool` (the runtime does not normalize its byte), `nint`/`nuint` (a 32-bit process would hash
  differently in this one place), `DateTimeOffset` (padding), and anything with a custom or default comparer.
- **Strings stay on Marvin** and the runtime's own hash.

## 8. Testing and tooling

- **Hash tests** that assert two values hash differently use fixed seeds and chosen vectors, since a legitimate
  collision under a random seed would fail the run; tests that assert equal values hash equal run over many seeds.
- **The storage-bits rule is tested on the semantic model**, not the text: `RawBitsTests` walks the emitted code for any
  conversion from a floating-point or decimal type to an integer, and any `GetHashCode` on one. A textual scan is fooled
  by comments and unrelated `unchecked` integer casts.
- **The generator test host always enables nullable**, which C# 7.3 rejects (CS8630), so it tests C# 9 and later; the
  fixtures compile the generated code at C# 7.3 and 8 on .NET Framework and .NET Core 3.1.
- **The `DEQ036` test's version override is async-local**, so it cannot leak into parallel tests.
- **Downstream restores into `downstream/.packages`**, cached by version. After repacking at an unchanged version,
  delete the `deepequals.*` folders there, or the old packages are used.
- **The local feed must hold one version.** `artifacts/package/release` can collect alpha-versioned packages from other
  builds; the smoke suite's `The_feed_holds_exactly_one_package` fails until they are deleted.
- **Profiling the generator.** `dotnet tool install -g dotnet-trace`, then a console project that references the
  generator project, builds the scale closure with `CSharpCompilation.Create` and runs `CSharpGeneratorDriver` over it
  several times (the first run pays for JIT and Roslyn's own caches), under
  `dotnet-trace collect --providers Microsoft-DotNETCore-SampleProfiler -- dotnet <runner>.dll`; then
  `dotnet-trace report <trace> topN -n 40 --inclusive` and without `--inclusive`. Time attributed to
  `Thread.PollGCWorker` is GC. With dotTrace installed (dotUltimate), the same runner under
  `ConsoleProfiler.exe start --profiling-type=Sampling --save-to=<snapshot>.dtp <full path to dotnet.exe> <runner>.dll`
  (the executable must be a full path), then `Reporter.exe report <snapshot>.dtp --pattern=<pattern>.xml
  --save-to=report.xml`, where the pattern file is `<Patterns><Pattern>DeepEquals.SourceGenerator.*</Pattern></Patterns>`,
  gives own and total time per function in XML; both tools live under
  `%LocalAppData%\JetBrains\Installations\dotTrace<version>`. The two profilers agreed on every hotspot.
- **BenchmarkDotNet truncates a scenario name** in its tables to its first five and last five characters with the
  length in brackets: `Dicti(...) x100 [26]` is `Dictionary<SkuId,int> x100` and `[31]` is `Dictionary<string,decimal>
  x100`. Read the length before attributing a row. A `--filter` glob matches the full name including the parameter, so
  `"*EqualityBenchmarks*_Equals*string,decimal*"` selects one scenario, and `--job Long` adds a `LongRun` job beside the
  config's `ShortRun` rather than replacing it.
- **The Mono smoke test needs `C:\Program Files\Mono\bin` on `PATH`** and skips otherwise. A browser test failing with
  `STATUS_INVALID_IMAGE_FORMAT` means a damaged Chromium headless shell;
  `playwright.ps1 install --force chromium-headless-shell` fixes it.
