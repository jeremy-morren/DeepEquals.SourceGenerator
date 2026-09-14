// Copyright 2026 The DeepEquals source generator project contributors. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System.Collections.Generic;

namespace DeepEquals.SourceGenerator.Model;

/// <summary>
/// Every identifier generated code derives from a type's short name, built here and nowhere else: the emitter writes
/// them through these methods, and <see cref="All"/> lists them so the analysis can reserve each against the user's
/// own members (DEQ016) and test that the reservation matches what is written.
/// </summary>
internal static class GeneratedNames
{
    /// <summary>The deepest fingerprint level a type may need: the option's maximum.</summary>
    public const int MaxMatchingHashDepth = ContextOptions.MaximumMatchingHashDepth;

    public static string Comparer(string shortName) => string.Concat(shortName, "EqualityComparer");

    public static string Equals(string shortName) => string.Concat("Equals_", shortName);

    public static string EqualsExact(string shortName) => string.Concat("EqualsExact_", shortName);

    public static string EqualsMembers(string shortName) => string.Concat("EqualsMembers_", shortName);

    public static string EqualsBoxed(string shortName) => string.Concat("EqualsBoxed_", shortName);

    public static string EqualsSpanOf(string shortName) => string.Concat("Equals_SpanOf", shortName);

    /// <summary>
    /// The full hash at level 1 (the public one), <c>ShallowHashCode_T</c> at level 0, and <c>MatchHashCode_T_L{n}</c>
    /// for the deeper levels the matching fingerprint uses.
    /// </summary>
    public static string Hash(string shortName, int level) => level switch
    {
        1 => string.Concat("GetHashCode_", shortName),
        0 => string.Concat("ShallowHashCode_", shortName),
        _ => $"MatchHashCode_{shortName}_L{level}",
    };

    /// <summary>The members-only hash of a class at <paramref name="level"/>, which dispatch calls after it has tested the runtime type.</summary>
    public static string MembersHash(string shortName, int level) => level switch
    {
        1 => string.Concat("HashMembers_", shortName),
        0 => string.Concat("ShallowHashMembers_", shortName),
        _ => $"MatchHashMembers_{shortName}_L{level}",
    };

    public static string Kind(string shortName) => string.Concat("Kind_", shortName);

    public static string BoxedKind(string shortName) => string.Concat("Kind_Boxed_", shortName);

    public static string Ops(string shortName) => string.Concat(shortName, "Ops");

    public static string ShallowOps(string shortName) => string.Concat(shortName, "ShallowOps");

    public static string Dispatch(string shortName) => string.Concat(shortName, "_Dispatch");

    public static string IsBitBlock(string shortName) => string.Concat(shortName, "_IsBitBlock");

    public static string Accessors(string shortName) => string.Concat(shortName, "_Accessors");

    public static string ComparerHolder(string shortName) => string.Concat(shortName, "_ComparerHolder");

    /// <summary>
    /// The sets built so far, by short name. Short names recur on every keystroke, and the analysis reads a type's
    /// set twice per run, so one build per name per process. Bounded, since a session can see many names: about fifty
    /// strings per entry, so the cap holds a few megabytes at most, and overflow starts over.
    /// </summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string[]> SAll = new(System.StringComparer.Ordinal);

    /// <summary>Every identifier a type claims from its short name: each name the emitter can write for it.</summary>
    public static string[] All(string shortName)
    {
        if (SAll.TryGetValue(shortName, out var cached))
            return cached;

        if (SAll.Count >= 4096)
            SAll.Clear();

        return SAll.GetOrAdd(shortName, Build(shortName));
    }

    private static string[] Build(string shortName)
    {
        var identifiers = new List<string>(20 + 2 * MaxMatchingHashDepth)
        {
            shortName,
            Comparer(shortName),
            Equals(shortName),
            EqualsExact(shortName),
            EqualsMembers(shortName),
            EqualsBoxed(shortName),
            EqualsSpanOf(shortName),
            Kind(shortName),
            BoxedKind(shortName),
            Ops(shortName),
            ShallowOps(shortName),
            ComparerHolder(shortName),
            Dispatch(shortName),
            Accessors(shortName),
            IsBitBlock(shortName),
        };
        for (var level = 0; level <= MaxMatchingHashDepth; level++)
        {
            identifiers.Add(Hash(shortName, level));
            identifiers.Add(MembersHash(shortName, level));
        }

        return identifiers.ToArray();
    }
}
