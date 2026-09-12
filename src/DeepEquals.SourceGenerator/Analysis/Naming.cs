// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DeepEquals.SourceGenerator.Analysis;

/// <summary>Short-name candidates and the collision rungs: arity, namespace, namespace plus arity, then a stable digest.</summary>
internal static class Naming
{
    public static readonly string[] HazardousNames = 
        ["Equals", "GetHashCode", "ReferenceEquals", "GetType", "ToString", "MemberwiseClone", "Finalize", "Instance", "Cache", "GetEqualityComparer"];

    public static readonly string[] ReservedNames =
        ["Cache", "GetEqualityComparer", "MaxComparisonPairs", "MaxUnorderedCollisionRun", "MaxDepth", "MatchingHashDepth", "ComparerMap"];

    /// <summary>The deepest fingerprint level a type may need; matches the option's maximum.</summary>
    public const int MaxMatchingHashDepth = 16;

    /// <summary>A C#-safe identifier for a name: <c>@</c>-prefixed when the name is a keyword.</summary>
    public static string Identifier(string name) =>
        SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? $"@{name}" : name;

    /// <summary>
    /// A type name as a declaration writes it: a keyword, or a name of lower-case ASCII letters only, is written with
    /// <c>@</c>. The compiler warns on every declaration of a lower-case type name (CS8981, such names may become
    /// keywords) unless it is verbatim, and the generator redeclares the user's types.
    /// </summary>
    public static string TypeIdentifier(string name) =>
        name.Length > 0 && name.All(c => c is >= 'a' and <= 'z') ? $"@{name}" : Identifier(name);

    /// <summary>Escapes one metadata-name segment: literal underscores and every non-identifier character become _uXXXX.</summary>
    public static string EscapeSegment(string segment)
    {
        StringBuilder? builder = null;
        for (var i = 0; i < segment.Length; i++)
        {
            var c = segment[i];
            var ok = c != '_' 
                     && (i == 0 ? SyntaxFacts.IsIdentifierStartCharacter(c) : SyntaxFacts.IsIdentifierPartCharacter(c));
            if (ok)
            {
                builder?.Append(c);
                continue;
            }

            if (builder is null)
            {
                builder = new StringBuilder(segment.Length + 8);
                builder.Append(segment, 0, i);
            }

            builder.Append("_u").Append(((int)c).ToString("X4"));
        }

        return builder?.ToString() ?? segment;
    }

    public static string Candidate(ITypeSymbol symbol, int rung)
    {
        var arity = rung is 1 or >= 3;
        var ns = rung >= 2;
        return Build(symbol, arity, ns);
    }

    private static string Build(ITypeSymbol symbol, bool arity, bool ns)
    {
        switch (symbol)
        {
            case IArrayTypeSymbol array:
                return $"ArrayOf{Build(array.ElementType, arity, ns)}";

            case INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable:
                return $"NullableOf{Build(nullable.TypeArguments[0], arity, ns)}";

            case INamedTypeSymbol named:
                var name = TypeName(named, ns);
                if (!named.IsGenericType)
                    return name;

                var builder = new StringBuilder(name);
                if (arity) 
                    builder.Append("Of").Append(named.TypeArguments.Length).Append('_');
                else
                    builder.Append("Of");

                for (var i = 0; i < named.TypeArguments.Length; i++)
                {
                    if (i > 0) 
                        builder.Append("And");

                    builder.Append(Build(named.TypeArguments[i], arity, ns));
                }

                return builder.ToString();

            default:
                return EscapeSegment(symbol.Name);
        }
    }

    private static string TypeName(INamedTypeSymbol named, bool ns)
    {
        var builder = new StringBuilder();
        if (ns)
        {
            var space = named.ContainingNamespace;
            var segments = new List<string>();
            while (space is not null && !space.IsGlobalNamespace)
            {
                segments.Add(EscapeSegment(space.Name));
                space = space.ContainingNamespace;
            }

            segments.Reverse();
            foreach (var segment in segments) 
                builder.Append(segment).Append('_');
        }

        var containing = new List<string>();
        for (var outer = named.ContainingType; outer is not null; outer = outer.ContainingType) 
            containing.Add(EscapeSegment(BareName(outer)));

        containing.Reverse();
        foreach (var segment in containing) 
            builder.Append(segment).Append('_');

        builder.Append(EscapeSegment(BareName(named)));
        return builder.ToString();
    }

    private static string BareName(INamedTypeSymbol named)
    {
        var metadata = named.MetadataName;
        var tick = metadata.IndexOf('`');
        return tick < 0 ? metadata : metadata[..tick];
    }

    /// <summary>
    /// The digest rung: the first <paramref name="digits"/> hex digits of SHA-256 over the assembly-qualified identity.
    /// </summary>
    public static string Digest(ITypeSymbol symbol, int digits) =>
        Digest($"{symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}, {symbol.ContainingAssembly?.Identity.GetDisplayName() ?? string.Empty}", digits);

    /// <summary>The first <paramref name="digits"/> hex digits of SHA-256 over <paramref name="identity"/>.</summary>
    public static string Digest(string identity, int digits)
    {
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(identity));
        var hex = new StringBuilder(digits);
        for (var i = 0; i < hash.Length && hex.Length < digits; i++) 
            hex.Append(hash[i].ToString("x2"));

        return hex.ToString(0, Math.Min(digits, hex.Length));
    }

    /// <summary>Every identifier a type claims from its short name: each name the emitter can write for it.</summary>
    public static string[] IdentifiersFor(string shortName)
    {
        var identifiers = new List<string>(24 + MaxMatchingHashDepth)
        {
            shortName,
            $"{shortName}EqualityComparer",
            $"Equals_{shortName}",
            $"EqualsExact_{shortName}",
            $"EqualsMembers_{shortName}",
            $"EqualsBody_{shortName}",
            $"EqualsBoxed_{shortName}",
            $"GetHashCode_{shortName}",
            $"ShallowHashCode_{shortName}",
            $"HashMembers_{shortName}",
            $"ShallowHashMembers_{shortName}",
            $"Equals_SpanOf{shortName}",
            $"GetHashCode_SpanOf{shortName}",
            $"Kind_{shortName}",
            $"Kind_Boxed_{shortName}",
            $"{shortName}Ops",
            $"{shortName}ShallowOps",
            $"{shortName}DepthOps",
            $"{shortName}_ComparerHolder",
            $"{shortName}_Dispatch",
            $"{shortName}_Accessors",
            $"{shortName}_IsBitBlock",
        };
        for (var level = 2; level <= MaxMatchingHashDepth; level++)
        {
            identifiers.Add($"MatchHashCode_{shortName}_L{level}");
            identifiers.Add($"MatchHashMembers_{shortName}_L{level}");
        }

        return identifiers.ToArray();
    }
}
