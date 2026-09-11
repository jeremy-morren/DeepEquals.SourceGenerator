// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using DeepEquals.SourceGeneration;
using DeepEquals.SourceGenerator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DeepEquals.Generator.Benchmarks;

public static class Program
{
    public static void Main(string[] args) => BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
}

/// <summary>In process: the generator is the code under test, so there is nothing to isolate it from.</summary>
public sealed class GeneratorConfig : ManualConfig
{
    public GeneratorConfig() => AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance));
}

/// <summary>A synthetic closure the generator can be run on, and the references a compilation of it needs.</summary>
internal static class Closures
{
    private static readonly Lazy<MetadataReference[]> s_references = new(() =>
    {
        var list = (string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!;
        return list.Split(Path.PathSeparator)
            .Where(f => Path.GetFileName(f) is var n && (n.StartsWith("System.", StringComparison.Ordinal) || n is "mscorlib.dll" or "netstandard.dll"))
            .Select(f => (MetadataReference)MetadataReference.CreateFromFile(f))
            .Append(MetadataReference.CreateFromFile(typeof(DeepEqualsContextBase).Assembly.Location))
            .ToArray();
    });

    /// <summary>
    /// <paramref name="types"/> sealed classes in one ring, each holding the next, a list of an earlier one and a
    /// dictionary of another: one large strongly connected component through classes and containers, the case guard
    /// selection and flag propagation work hardest on.
    /// </summary>
    public static CSharpCompilation Build(int types, bool everyTypeARoot)
    {
        var source = new StringBuilder("using System; using System.Collections.Generic; using DeepEquals.SourceGeneration; namespace Scale;\n");
        for (var i = 0; i < types; i++)
        {
            source.Append($"public sealed class C{i} {{ public int V; public string? S; public double D; public C{(i + 1) % types}? Next;");
            if (i % 5 == 0) source.Append($" public List<C{i / 2}>? Items;");
            if (i % 7 == 0) source.Append($" public Dictionary<string, C{(i * 3) % types}>? Map;");
            source.Append(" }\n");
        }

        if (everyTypeARoot)
            for (var i = 0; i < types; i++) source.Append($"[GenerateDeepEquals(typeof(C{i}))]\n");
        else
            source.Append("[GenerateDeepEquals(typeof(C0))]\n");

        source.Append("public partial class Ctx : DeepEqualsContextBase { }\n");
        return CSharpCompilation.Create(
            $"Scale{types}{(everyTypeARoot ? "All" : "One")}",
            [CSharpSyntaxTree.ParseText(source.ToString(), new CSharpParseOptions(LanguageVersion.Latest))],
            s_references.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
    }

    public static GeneratorDriver Driver() =>
        CSharpGeneratorDriver.Create([new DeepEqualsGenerator().AsSourceGenerator()], parseOptions: new CSharpParseOptions(LanguageVersion.Latest));
}

/// <summary>Elapsed time and allocations of one generator run from scratch; the generated size is written to the log.</summary>
[Config(typeof(GeneratorConfig))]
[MemoryDiagnoser]
public class GeneratorScaleBenchmarks
{
    private CSharpCompilation _compilation = null!;

    [Params(100, 1000, 4000)]
    public int Types { get; set; }

    [Params(false, true)]
    public bool EveryTypeARoot { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _compilation = Closures.Build(Types, EveryTypeARoot);
        var result = Closures.Driver().RunGenerators(_compilation).GetRunResult();
        var sources = result.Results.SelectMany(r => r.GeneratedSources).ToList();
        var errors = result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        Console.WriteLine($"// {Types} types, every type a root: {EveryTypeARoot}: {sources.Count} files, {sources.Sum(s => s.SourceText.Length):N0} characters generated, {errors.Count} errors");
        foreach (var error in errors.Take(3)) Console.WriteLine($"//   {error}");
    }

    [Benchmark]
    public int Generate() => Closures.Driver().RunGenerators(_compilation).GetRunResult().Results.Sum(r => r.GeneratedSources.Length);
}

/// <summary>
/// How quickly a cancelled run returns: the token is cancelled 20 ms into generating the largest closure, so the mean
/// minus 20 ms is the time the generator took to notice. Exposes cancellation checks missing from long loops.
/// </summary>
[Config(typeof(GeneratorConfig))]
public class CancellationBenchmarks
{
    private CSharpCompilation _compilation = null!;

    [GlobalSetup]
    public void Setup() => _compilation = Closures.Build(4000, everyTypeARoot: true);

    [Benchmark]
    public bool CancelAfter20Milliseconds()
    {
        using var source = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));
        try
        {
            Closures.Driver().RunGenerators(_compilation, source.Token);
            return false;   // finished inside 20 ms: nothing to cancel
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }
}
