; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
DEQ001 | DeepEquals | Error | Context must be partial
DEQ002 | DeepEquals | Error | Invalid context declaration
DEQ003 | DeepEquals | Error | Member type is not accessible from the context
DEQ004 | DeepEquals | Warning | Enumerable member may be lazy or non-repeatable
DEQ005 | DeepEquals | Warning | Delegate or event member skipped
DEQ006 | DeepEquals | Warning | Custom comparer must implement exactly one IEqualityComparer<T>
DEQ007 | DeepEquals | Warning | Custom comparer has no valid acquisition path
DEQ008 | DeepEquals | Warning | Overlapping strategy registrations
DEQ009 | DeepEquals | Warning | Two equally specific interface comparers apply
DEQ010 | DeepEquals | Error | Invalid registered type
DEQ011 | DeepEquals | Error | Generated identifiers collide
DEQ012 | DeepEquals | Error | Unsupported member type
DEQ013 | DeepEquals | Warning | Invalid generation option
DEQ014 | DeepEquals | Error | Multi-dimensional arrays are not supported
DEQ015 | DeepEquals | Warning | Ambiguous collection shape
DEQ016 | DeepEquals | Error | Generated identifier collides with a user member
DEQ017 | DeepEquals | Error | Type comes from a reference assembly
DEQ018 | DeepEquals | Error | Closure construction exceeded its bounds
DEQ019 | DeepEquals | Warning | handleNulls on a non-nullable value type
DEQ020 | DeepEquals | Warning | No concrete type can satisfy this dispatch
DEQ021 | DeepEquals | Warning | Convenience property name would hide a familiar member
DEQ022 | DeepEquals | Warning | Regex compares by reference
DEQ023 | DeepEquals | Warning | dynamic member ignored
DEQ024 | DeepEquals | Warning | Framework type compared by its fields
DEQ025 | DeepEquals | Warning | Overlapping [SimpleType] registrations
DEQ026 | DeepEquals | Warning | Struct comes from a reference assembly
DEQ027 | DeepEquals | Warning | Collection-shaped type has storage that will be ignored
DEQ028 | DeepEquals | Warning | [SimpleType] names a nullable value type
DEQ029 | DeepEquals | Warning | Custom comparer targets a nullable value type without handling null
DEQ030 | DeepEquals | Warning | Overlapping [CustomEqualityComparer] registrations
DEQ031 | DeepEquals | Error | Ambiguous custom comparers
DEQ032 | DeepEquals | Error | Fixed buffers and inline arrays are not supported
DEQ033 | DeepEquals | Warning | Context-level ignore matched no member
DEQ034 | DeepEquals | Warning | [DeepEqualsIgnore] on a member without storage
DEQ035 | DeepEquals | Warning | Custom comparer for object
DEQ036 | DeepEquals | Error | Framework package version does not match the generator
DEQ037 | DeepEquals | Warning | Generation option has no effect
DEQ099 | DeepEquals | Error | The generator failed
