# DeepEquals.SourceGenerator

A Roslyn source generator that emits deep, by-value `IEqualityComparer<T>` implementations for a closed set of types:
cycle-safe, comparing instance storage rather than property getters, and avoiding steady-state allocations wherever the runtime allows.

## Quickstart

```xml
<PackageReference Include="DeepEquals.SourceGenerator" Version="1.0.0-beta05" PrivateAssets="all" />
<PackageReference Include="DeepEquals.SourceGeneration.Framework" Version="1.0.0-beta05" />
```

```csharp
using DeepEquals.SourceGeneration;

[GenerateDeepEquals(typeof(Customer))]
[GenerateDeepEquals(typeof(Order))]
public partial class MyDeepEqualsContext : DeepEqualsContextBase { }

bool same = MyDeepEqualsContext.Customer.Equals(a, b);
int hash  = MyDeepEqualsContext.Customer.GetHashCode(a);
IEqualityComparer<Order> orders = MyDeepEqualsContext.GetEqualityComparer<Order>();
```

Registering a type also registers every type it reaches: members, collection elements, base classes and interfaces. Each gets a property on the context.
Keep both packages at the same version. The project must be C# on Roslyn 4.3.1 or later (.NET SDK 6.0.400, Visual Studio 17.3).

## Choosing a cycle mode

```csharp
[DeepEqualsSourceGenerationOptions(CycleHandling = DeepEqualsCycleHandling.Tree)]
public partial class MyDeepEqualsContext : DeepEqualsContextBase { }
```

- **`Graph`** (default) remembers every pair of objects it has compared. Cycles terminate and a shared subgraph is compared once, at the cost of a table lookup per pair.
- **`Path`** remembers only the pairs on the current path. Same answers as `Graph` with less memory, but a heavily shared graph can be compared exponentially many times.
- **`Tree`** remembers nothing, so there is no table or pair budget. Use it for data that cannot hold cycles, such as deserialized payloads; a cycle throws `DeepEqualsComplexityException`.

## Before you start

- **Derived types are not discovered.** Register every runtime type a member typed as `object`, an interface or a non-sealed class can hold; an unregistered one throws `DeepEqualsUnknownTypeException`.
- **Seal your classes.** An unsealed class is dispatched on its runtime type at every comparison; a sealed one is compared directly.
- **Types from another project** need `<CompileUsingReferenceAssemblies>false</CompileUsingReferenceAssemblies>` in the project that declares the context, or their private fields are invisible ([`DEQ017`](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Diagnostics.md#referenced-assemblies)).
- **Every field is compared**, public and private, across the whole base chain. Getters are never called. Exclude one with `[DeepEqualsIgnore]`.
- **Equality is exact.** Strings compare ordinally, floating point bitwise (`NaN` equals itself, `+0` differs from `-0`), and `1.0m` differs from `1.00m`.
- **The declared type decides collection semantics.** `ISet<T>` and dictionaries compare unordered, everything else in order, and a collection's own comparer is ignored.

Where the built-in rule is wrong for a type, register `[SimpleType(typeof(T))]` to use its own `Equals`, or `[CustomEqualityComparer(typeof(TComparer))]` to supply a comparer.

## Target limitations

| Consumer target                                        | Limitation                                                                                                                                                                                     |
|--------------------------------------------------------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| .NET Framework                                         | Needs `<AutoGenerateBindingRedirects>true</AutoGenerateBindingRedirects>` and `<GenerateBindingRedirectsOutputType>true</GenerateBindingRedirectsOutputType>`.                                 |
| Below `net8.0`                                         | Private fields are read through delegates compiled from expression trees, built once per field. Comparers that need them carry `[RequiresUnreferencedCode]` and `[RequiresDynamicCode]`. |
| `net8.0`                                               | The same, for fields of generic types only.                                                                                                                                                    |
| `netstandard2.1`, `netcoreapp3.x`, `net5.0`            | Arrays and lists of plain values (integers, enums, `Guid`, ...) are compared and hashed element by element rather than as one block of memory.                                                 |

## More

- [Reference](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Reference.md): every attribute and option, the exact equality relation, exceptions, limits and project setup
- [Diagnostics](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Diagnostics.md): every `DEQ` id, when it fires and how to fix it
- [Implementation](https://github.com/jeremy-morren/DeepEquals.SourceGenerator/blob/main/docs/Implementation.md): the generated code and the runtime library
