// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using Microsoft.CodeAnalysis;

namespace DeepEquals.SourceGenerator;

/// <summary>Every DEQ diagnostic, in the order of the design's diagnostics table.</summary>
internal static class Diagnostics
{
    private const string Category = "DeepEquals";

    private static DiagnosticDescriptor Error(string id, string title, string message)
        => new(id, title, message, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

    private static DiagnosticDescriptor Warning(string id, string title, string message)
        => new(id, title, message, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor ContextNotPartial = Error(
        "DEQ001", "Context must be partial",
        "The deep-equality context '{0}' must be declared 'partial' so the generated half can be added");

    public static readonly DiagnosticDescriptor InvalidContextDeclaration = Error(
        "DEQ002", "Invalid context declaration",
        "'{0}' cannot be a deep-equality context: {1}");

    public static readonly DiagnosticDescriptor InaccessibleMemberType = Error(
        "DEQ003", "Member type is not accessible from the context",
        "Member '{0}' of '{1}' has type '{2}', which the context cannot name; expose the type, add InternalsVisibleTo, or exclude the member with [DeepEqualsIgnore] or a context-level [DeepEqualsIgnore(typeof({1}), \"{0}\")]");

    public static readonly DiagnosticDescriptor PossiblyLazyEnumerable = Warning(
        "DEQ004", "Enumerable member may be lazy or non-repeatable",
        "Member '{0}' of '{1}' is declared as '{2}' and is compared as an ordered sequence; a lazy or single-use sequence will be enumerated separately for equality and hashing");

    public static readonly DiagnosticDescriptor DelegateOrEventSkipped = Warning(
        "DEQ005", "Delegate or event member skipped",
        "Member '{0}' of '{1}' is a delegate or event and is not compared");

    public static readonly DiagnosticDescriptor CustomComparerInterfaceCount = Warning(
        "DEQ006", "Custom comparer must implement exactly one IEqualityComparer<T>",
        "'{0}' implements {1} IEqualityComparer<T> interface(s) ({2}); the [CustomEqualityComparer] registration is ignored");

    public static readonly DiagnosticDescriptor CustomComparerNoAcquisition = Warning(
        "DEQ007", "Custom comparer has no valid acquisition path",
        "'{0}' cannot be instantiated for [CustomEqualityComparer]: {1}; the registration is ignored");

    public static readonly DiagnosticDescriptor StrategyOverlap = Warning(
        "DEQ008", "Overlapping strategy registrations",
        "{0}");

    public static readonly DiagnosticDescriptor AmbiguousInterfaceComparers = Warning(
        "DEQ009", "Two equally specific interface comparers apply",
        "Member '{0}' of '{1}' is covered by both '{2}' and '{3}'; the first in source order is used");

    public static readonly DiagnosticDescriptor InvalidRegisteredType = Error(
        "DEQ010", "Invalid registered type",
        "'{0}' cannot be registered: {1}");

    public static readonly DiagnosticDescriptor IdentifierCollision = Error(
        "DEQ011", "Generated identifiers collide",
        "Types '{0}' and '{1}' produce the same generated identifier '{2}' after every collision-resolution step");

    public static readonly DiagnosticDescriptor UnsupportedMemberType = Error(
        "DEQ012", "Unsupported member type",
        "Member '{0}' of '{1}' has type '{2}', which {3} and cannot be compared; exclude it with [DeepEqualsIgnore]");

    public static readonly DiagnosticDescriptor InvalidOption = Warning(
        "DEQ013", "Invalid generation option",
        "{0}");

    public static readonly DiagnosticDescriptor MultiDimensionalArray = Error(
        "DEQ014", "Multi-dimensional arrays are not supported",
        "Member '{0}' of '{1}' has type '{2}'; multi-dimensional arrays are not supported, register a [CustomEqualityComparer] for the array type or exclude the member");

    public static readonly DiagnosticDescriptor AmbiguousCollectionShape = Warning(
        "DEQ015", "Ambiguous collection shape",
        "'{0}' implements more than one instantiation of {1} ({2}); '{3}' is used. Register a [CustomEqualityComparer] for the type to compare it as a whole");

    public static readonly DiagnosticDescriptor UserMemberCollision = Error(
        "DEQ016", "Generated identifier collides with a user member",
        "The context declares a member named '{0}', which the generator also needs for '{1}'; rename the user member");

    public static readonly DiagnosticDescriptor ReferenceAssemblyClass = Error(
        "DEQ017", "Type comes from a reference assembly",
        "'{0}' comes from a reference assembly, whose private state is stripped, so its members cannot be compared. For a ProjectReference set <CompileUsingReferenceAssemblies>false</CompileUsingReferenceAssemblies> in the context project; for a package ref/ assembly that property has no effect and only [SimpleType] or [CustomEqualityComparer] apply");

    public static readonly DiagnosticDescriptor ClosureTooLarge = Error(
        "DEQ018", "Closure construction exceeded its bounds",
        "Closure construction stopped at '{0}' reached through {1}: {2}");

    public static readonly DiagnosticDescriptor HandleNullsOnValueType = Warning(
        "DEQ019", "handleNulls on a non-nullable value type",
        "[CustomEqualityComparer] for '{0}' sets handleNulls: true, but a non-nullable value type has no null; the flag is ignored");

    public static readonly DiagnosticDescriptor NoDispatchCases = Warning(
        "DEQ020", "No concrete type can satisfy this dispatch",
        "'{0}' is abstract or an interface and no concrete type in the closure is assignable to it; every non-null value will throw DeepEqualsUnknownTypeException. Register the possible runtime types");

    public static readonly DiagnosticDescriptor HazardousConvenienceName = Warning(
        "DEQ021", "Convenience property name would hide a familiar member",
        "Type '{0}' would produce a convenience property named '{1}', which hides '{1}'; the property is omitted, the wrapper and GetEqualityComparer<T>() remain");

    public static readonly DiagnosticDescriptor RegexReferenceEquality = Warning(
        "DEQ022", "Regex compares by reference",
        "'{0}' reaches System.Text.RegularExpressions.Regex, which is compared by reference identity; register an IEqualityComparer<Regex> with [CustomEqualityComparer] for pattern semantics");

    public static readonly DiagnosticDescriptor DynamicMember = Warning(
        "DEQ023", "dynamic member ignored",
        "Member '{0}' of '{1}' has a type containing 'dynamic' and is ignored");

    public static readonly DiagnosticDescriptor FrameworkTypeWalked = Warning(
        "DEQ024", "Framework type compared by its fields",
        "'{0}' is a System type that is neither a built-in leaf nor a recognized shape and will be compared through its visible fields, which may be reference-assembly placeholders; register it with [SimpleType] or a [CustomEqualityComparer]");

    public static readonly DiagnosticDescriptor SimpleTypeOverlap = Warning(
        "DEQ025", "Overlapping [SimpleType] registrations",
        "[SimpleType] for '{0}' is covered by [SimpleType] for '{1}' and is ignored");

    public static readonly DiagnosticDescriptor ReferenceAssemblyStruct = Warning(
        "DEQ026", "Struct comes from a reference assembly",
        "'{0}' comes from a reference assembly; its visible layout fields are compared, which may omit private state. Prefer the implementation assembly, or register [SimpleType] or a [CustomEqualityComparer]");

    public static readonly DiagnosticDescriptor CollectionShapeIgnoresStorage = Warning(
        "DEQ027", "Collection-shaped type has storage that will be ignored",
        "'{0}' is compared as {1} and its own instance storage ({2}) is ignored; register a [CustomEqualityComparer] for whole-object semantics");

    public static readonly DiagnosticDescriptor SimpleTypeNullable = Warning(
        "DEQ028", "[SimpleType] names a nullable value type",
        "[SimpleType(typeof({0}?))] is normalized to [SimpleType(typeof({0}))], which covers both '{0}' and '{0}?'");

    public static readonly DiagnosticDescriptor NullableCustomComparerWithoutNulls = Warning(
        "DEQ029", "Custom comparer targets a nullable value type without handling null",
        "[CustomEqualityComparer] for '{0}?' has handleNulls: false, so it never receives null; register a comparer for '{0}' instead");

    public static readonly DiagnosticDescriptor CustomComparerOverlap = Warning(
        "DEQ030", "Overlapping [CustomEqualityComparer] registrations",
        "[CustomEqualityComparer] for '{0}' is covered by the registration for '{1}' and is ignored; '{1}' and its null policy apply");

    public static readonly DiagnosticDescriptor AmbiguousCustomInterfaceComparers = Error(
        "DEQ031", "Ambiguous custom comparers",
        "'{0}' is covered by unrelated [CustomEqualityComparer] registrations for '{1}' and '{2}'; remove one or register '{0}' directly");

    public static readonly DiagnosticDescriptor UnsupportedStorage = Error(
        "DEQ032", "Fixed buffers and inline arrays are not supported",
        "Member '{0}' of '{1}' is a fixed buffer or an [InlineArray] struct; a field walk would compare only its first element. Exclude it with [DeepEqualsIgnore], a context-level ignore, or a [CustomEqualityComparer] for '{1}'");

    public static readonly DiagnosticDescriptor ContextIgnoreMatchedNothing = Warning(
        "DEQ033", "Context-level ignore matched no member",
        "[DeepEqualsIgnore(typeof({0}), \"{1}\")] matches no selected field or stored property on '{0}'");

    public static readonly DiagnosticDescriptor IgnoreOnComputedMember = Warning(
        "DEQ034", "[DeepEqualsIgnore] on a member without storage",
        "'{0}' on '{1}' has no compiler storage and was never compared; the [DeepEqualsIgnore] has no effect");

    public static readonly DiagnosticDescriptor ObjectCustomComparer = Warning(
        "DEQ035", "Custom comparer for object",
        "[CustomEqualityComparer] for 'object' would make every reference type a leaf and is ignored");

    public static readonly DiagnosticDescriptor GeneratorFailed = Error(
        "DEQ099", "The generator failed",
        "The deep-equality generator threw while {0} context '{1}': {2}: {3}");
}
