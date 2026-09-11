// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using DeepEquals.SourceGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DeepEquals.SourceGenerator.Tests;

/// <summary>Runs the generator over source against the executing runtime's references, then compiles and loads the result.</summary>
internal sealed class GeneratorRun
{
    public GeneratorRun(GeneratorDriverRunResult result, Compilation output, ImmutableArray<Diagnostic> compileDiagnostics, Assembly? assembly)
    {
        Result = result;
        Output = output;
        CompileDiagnostics = compileDiagnostics;
        Assembly = assembly;
    }

    public GeneratorDriverRunResult Result { get; }

    public Compilation Output { get; }

    public ImmutableArray<Diagnostic> CompileDiagnostics { get; }

    public Assembly? Assembly { get; }

    public IEnumerable<Diagnostic> GeneratorDiagnostics => Result.Diagnostics;

    public string GeneratedSource => string.Join("\n\n", Result.GeneratedTrees.Select(t => t.ToString()));

    public IEnumerable<string> GeneratorDiagnosticIds => GeneratorDiagnostics.Select(d => d.Id);

    public IEnumerable<string> CompileErrors => CompileDiagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.ToString());

    /// <summary>The context type from the loaded assembly.</summary>
    public Type Context(string name) => Assembly!.GetTypes().Single(t => t.Name == name);

    /// <summary>Reads a convenience property and returns the comparer instance.</summary>
    public object Comparer(string contextName, string propertyName)
        => Context(contextName).GetProperty(propertyName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;

    public bool Equals(object comparer, object? x, object? y)
        => (bool)comparer.GetType().GetMethod("Equals", BindingFlags.Public | BindingFlags.Instance, null, comparer.GetType().GetInterfaces().First(i => i.IsGenericType).GetGenericArguments().Select(a => a).Concat(comparer.GetType().GetInterfaces().First(i => i.IsGenericType).GetGenericArguments()).ToArray(), null)!.Invoke(comparer,
            [x, y])!;

    public int Hash(object comparer, object? value)
        => (int)comparer.GetType().GetMethod("GetHashCode", BindingFlags.Public | BindingFlags.Instance, null, comparer.GetType().GetInterfaces().First(i => i.IsGenericType).GetGenericArguments(), null)!.Invoke(comparer,
            [value])!;

    public object New(string typeName, params object?[] args)
    {
        var type = Assembly!.GetTypes().Single(t => t.Name == typeName);
        return Activator.CreateInstance(type, args)!;
    }
}

internal static class GeneratorHost
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> s_references = new(LoadReferences);

    /// <summary>The executing runtime's managed references plus the framework assembly.</summary>
    public static ImmutableArray<MetadataReference> References => s_references.Value;

    private static ImmutableArray<MetadataReference> LoadReferences()
    {
        // The trusted platform assembly list holds managed assemblies only, unlike the runtime directory.
        var list = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        var files = list.Split(Path.PathSeparator);
        var references = new List<MetadataReference>(files.Length + 1);
        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            if (name.StartsWith("System.", StringComparison.Ordinal) || name == "mscorlib.dll" || name == "netstandard.dll" || name == "Microsoft.CSharp.dll")
            {
                references.Add(MetadataReference.CreateFromFile(file));
            }
        }

        references.Add(MetadataReference.CreateFromFile(typeof(DeepEqualsContextBase).Assembly.Location));
        return [..references];
    }

    public static GeneratorRun Run(string source, LanguageVersion languageVersion = LanguageVersion.Latest, bool load = true, string assemblyName = "GeneratedTests", bool checkedArithmetic = false)
    {
        var parseOptions = new CSharpParseOptions(languageVersion);
        var tree = CSharpSyntaxTree.ParseText(source, parseOptions, path: "Input.cs");
        var compilation = CSharpCompilation.Create(
            $"{assemblyName}_{Guid.NewGuid():N}",
            [tree],
            s_references.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true, checkOverflow: checkedArithmetic));

        GeneratorDriver driver = CSharpGeneratorDriver.Create([new DeepEqualsGenerator().AsSourceGenerator()], parseOptions: parseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out _);
        var result = driver.GetRunResult();

        var compileDiagnostics = output.GetDiagnostics();
        Assembly? assembly = null;
        if (load && !compileDiagnostics.Any(d => d.Severity == DiagnosticSeverity.Error) && !result.Diagnostics.Any(d => d.Severity == DiagnosticSeverity.Error))
        {
            using var stream = new MemoryStream();
            var emit = output.Emit(stream);
            if (emit.Success) assembly = Assembly.Load(stream.ToArray());
            else compileDiagnostics = emit.Diagnostics;
        }

        return new GeneratorRun(result, output, compileDiagnostics, assembly);
    }
}
