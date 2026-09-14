// Copyright 2026 The DeepEquals source generator project contributors. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DeepEquals.SourceGenerator.Analysis;

/// <summary>
/// A hash of everything in a compilation that the analysis can see: every declaration, the references and the
/// options, but not the bodies of methods, constructors and operators, which declare no storage. The incremental
/// driver runs the attribute transform on every compilation, so every keystroke; two compilations with one fingerprint
/// produce one model, and the analysis runs once for both.
/// </summary>
/// <remarks>
/// A body is kept where it can declare storage: an accessor body, since <c>field</c> in it creates a backing field, and
/// every body of a type with a primary constructor, since a member that uses a parameter captures it into a field.
/// Directives are kept, since <c>#nullable</c> changes what a declaration means. Each tree's hash is cached on the tree
/// object, which the driver reuses for an unchanged file, so a keystroke costs one tree's walk.
/// </remarks>
internal static class DeclarationFingerprint
{
    private static readonly ConditionalWeakTable<SyntaxTree, StrongBox<ulong>> STrees = new();
    private static readonly ConditionalWeakTable<Compilation, StrongBox<ulong>> SCompilations = new();

    public static ulong Of(Compilation compilation) => SCompilations.GetValue(compilation, static c => new StrongBox<ulong>(Compute(c))).Value;

    private static ulong Compute(Compilation compilation)
    {
        var hash = Fnv.Start;
        hash = Fnv.Add(hash, compilation.AssemblyName ?? string.Empty);
        hash = Fnv.Add(hash, (int)compilation.Options.NullableContextOptions);
        hash = Fnv.Add(hash, compilation.Options.CheckOverflow ? 1 : 0);
        foreach (var tree in compilation.SyntaxTrees)
        {
            hash = Fnv.Add(hash, STrees.GetValue(tree, static t => new StrongBox<ulong>(TreeHash(t))).Value);
            if (tree.Options is CSharpParseOptions options)
                hash = Fnv.Add(hash, (int)options.LanguageVersion);
        }

        foreach (var reference in compilation.References)
        {
            // A project reference is another compilation, fingerprinted the same way; a metadata reference object is
            // reused by the host until its file changes, so its identity stands for its contents within this process.
            hash = reference is CompilationReference project
                ? Fnv.Add(hash, Of(project.Compilation))
                : Fnv.Add(hash, RuntimeHelpers.GetHashCode(reference));
        }

        return hash;
    }

    private static ulong TreeHash(SyntaxTree tree)
    {
        var hasher = new Hasher();
        hasher.Visit(tree.GetRoot());
        return hasher.Hash;
    }

    private sealed class Hasher : CSharpSyntaxWalker
    {
        public ulong Hash = Fnv.Start;

        /// <summary>Inside a type with a primary constructor, whose member bodies can capture its parameters.</summary>
        private int _primaryConstructorTypes;

        public Hasher() : base(SyntaxWalkerDepth.StructuredTrivia) { }

        public override void VisitToken(SyntaxToken token)
        {
            Hash = Fnv.Add(Hash, token.RawKind);
            Hash = Fnv.Add(Hash, token.Text);
            base.VisitToken(token);
        }

        public override void VisitTrivia(SyntaxTrivia trivia)
        {
            // Directives shape declarations; comments and whitespace do not.
            if (trivia.HasStructure)
                base.VisitTrivia(trivia);
        }

        public override void VisitBlock(BlockSyntax node)
        {
            if (!Strips(node))
                base.VisitBlock(node);
        }

        public override void VisitArrowExpressionClause(ArrowExpressionClauseSyntax node)
        {
            if (!Strips(node))
                base.VisitArrowExpressionClause(node);
        }

        private bool Strips(SyntaxNode body) =>
            _primaryConstructorTypes == 0 &&
            body.Parent is MethodDeclarationSyntax or ConstructorDeclarationSyntax or DestructorDeclarationSyntax
                or OperatorDeclarationSyntax or ConversionOperatorDeclarationSyntax;

        public override void VisitClassDeclaration(ClassDeclarationSyntax node) => VisitType(node, base.VisitClassDeclaration);

        public override void VisitStructDeclaration(StructDeclarationSyntax node) => VisitType(node, base.VisitStructDeclaration);

        public override void VisitRecordDeclaration(RecordDeclarationSyntax node) => VisitType(node, base.VisitRecordDeclaration);

        /// <summary>
        /// A primary constructor is a parameter list directly under the type:
        /// found by kind, since the generator's Roslyn predates one on classes.
        /// </summary>
        private static bool HasPrimaryConstructor(TypeDeclarationSyntax node) => 
            node.ChildNodes().Any(child => child is ParameterListSyntax);

        private void VisitType<T>(T node, System.Action<T> visit) where T : TypeDeclarationSyntax
        {
            if (!HasPrimaryConstructor(node))
            {
                visit(node);
                return;
            }

            _primaryConstructorTypes++;
            visit(node);
            _primaryConstructorTypes--;
        }
    }

    /// <summary>FNV-1a over 64 bits: stable within a process, which is as long as the cache lives.</summary>
    private static class Fnv
    {
        public const ulong Start = 14695981039346656037UL;
        private const ulong Prime = 1099511628211UL;

        public static ulong Add(ulong hash, string text)
        {
            hash = text.Aggregate(hash, (current, c) => (current ^ c) * Prime);
            return (hash ^ 0xFF) * Prime;
        }

        public static ulong Add(ulong hash, int value) => (hash ^ (uint)value) * Prime;

        public static ulong Add(ulong hash, ulong value)
        {
            hash = (hash ^ (uint)value) * Prime;
            return (hash ^ (uint)(value >> 32)) * Prime;
        }
    }
}
