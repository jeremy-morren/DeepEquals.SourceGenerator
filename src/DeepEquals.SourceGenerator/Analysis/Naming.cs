using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace DeepEquals.SourceGenerator.Analysis;

/// <summary>Short-name candidates and the collision rungs: arity, namespace, namespace plus arity, then a stable digest.</summary>
internal static class Naming
{
    public static readonly string[] HazardousNames =
    {
        "Equals", "GetHashCode", "ReferenceEquals", "GetType", "ToString", "MemberwiseClone", "Finalize", "Instance", "Cache", "GetEqualityComparer",
    };

    public static readonly string[] ReservedNames = { "Cache", "GetEqualityComparer", "MaxComparisonPairs", "MaxUnorderedCollisionRun", "ComparerMap" };

    /// <summary>Escapes one metadata-name segment: literal underscores and every non-identifier character become _uXXXX.</summary>
    public static string EscapeSegment(string segment)
    {
        StringBuilder? builder = null;
        for (int i = 0; i < segment.Length; i++)
        {
            char c = segment[i];
            bool ok = c == '_' ? false : i == 0 ? SyntaxFacts.IsIdentifierStartCharacter(c) : SyntaxFacts.IsIdentifierPartCharacter(c);
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

    /// <summary>Prefixes a keyword with @ so it is usable as an identifier.</summary>
    public static string Identifier(string name)
        => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(name) == SyntaxKind.None && false ? "@" + name : name;

    public static string Candidate(ITypeSymbol symbol, int rung)
    {
        bool arity = rung == 1 || rung >= 3;
        bool ns = rung >= 2;
        return Build(symbol, arity, ns);
    }

    private static string Build(ITypeSymbol symbol, bool arity, bool ns)
    {
        switch (symbol)
        {
            case IArrayTypeSymbol array:
                return "ArrayOf" + Build(array.ElementType, arity, ns);

            case INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable:
                return "NullableOf" + Build(nullable.TypeArguments[0], arity, ns);

            case INamedTypeSymbol named:
                string name = TypeName(named, ns);
                if (!named.IsGenericType)
                {
                    return name;
                }

                StringBuilder builder = new StringBuilder(name);
                if (arity)
                {
                    builder.Append("Of").Append(named.TypeArguments.Length).Append('_');
                }
                else
                {
                    builder.Append("Of");
                }

                for (int i = 0; i < named.TypeArguments.Length; i++)
                {
                    if (i > 0)
                    {
                        builder.Append("And");
                    }

                    builder.Append(Build(named.TypeArguments[i], arity, ns));
                }

                return builder.ToString();

            default:
                return EscapeSegment(symbol.Name);
        }
    }

    private static string TypeName(INamedTypeSymbol named, bool ns)
    {
        StringBuilder builder = new StringBuilder();
        if (ns)
        {
            INamespaceSymbol? space = named.ContainingNamespace;
            List<string> segments = new List<string>();
            while (space is not null && !space.IsGlobalNamespace)
            {
                segments.Add(EscapeSegment(space.Name));
                space = space.ContainingNamespace;
            }

            segments.Reverse();
            foreach (string segment in segments)
            {
                builder.Append(segment).Append('_');
            }
        }

        List<string> containing = new List<string>();
        for (INamedTypeSymbol? outer = named.ContainingType; outer is not null; outer = outer.ContainingType)
        {
            containing.Add(EscapeSegment(BareName(outer)));
        }

        containing.Reverse();
        foreach (string segment in containing)
        {
            builder.Append(segment).Append('_');
        }

        builder.Append(EscapeSegment(BareName(named)));
        return builder.ToString();
    }

    private static string BareName(INamedTypeSymbol named)
    {
        string metadata = named.MetadataName;
        int tick = metadata.IndexOf('`');
        return tick < 0 ? metadata : metadata.Substring(0, tick);
    }

    /// <summary>The digest rung: the first <paramref name="digits"/> hex digits of SHA-256 over the assembly-qualified identity.</summary>
    public static string Digest(ITypeSymbol symbol, int digits)
    {
        string identity = symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ", " + (symbol.ContainingAssembly?.Identity.GetDisplayName() ?? string.Empty);
        using (SHA256 sha = SHA256.Create())
        {
            byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(identity));
            StringBuilder hex = new StringBuilder(digits);
            for (int i = 0; i < hash.Length && hex.Length < digits; i++)
            {
                hex.Append(hash[i].ToString("x2"));
            }

            return hex.ToString(0, Math.Min(digits, hex.Length));
        }
    }

    /// <summary>Every identifier a type claims from its short name.</summary>
    public static IEnumerable<string> IdentifiersFor(string shortName)
    {
        yield return shortName;
        yield return shortName + "EqualityComparer";
        yield return "Equals_" + shortName;
        yield return "EqualsExact_" + shortName;
        yield return "EqualsMembers_" + shortName;
        yield return "EqualsBoxed_" + shortName;
        yield return "GetHashCode_" + shortName;
        yield return "ShallowHashCode_" + shortName;
        yield return "Equals_SpanOf" + shortName;
        yield return "GetHashCode_SpanOf" + shortName;
        yield return "Kind_" + shortName;
        yield return "Kind_Boxed_" + shortName;
        yield return shortName + "Ops";
        yield return shortName + "ShallowOps";
        yield return shortName + "_ComparerHolder";
        yield return shortName + "_Dispatch";
        yield return shortName + "_Accessors";
    }
}
