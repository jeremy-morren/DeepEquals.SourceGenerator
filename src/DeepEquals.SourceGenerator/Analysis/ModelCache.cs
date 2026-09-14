// Copyright 2026 The DeepEquals source generator project contributors. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Collections.Generic;
using DeepEquals.SourceGenerator.Model;

namespace DeepEquals.SourceGenerator.Analysis;

/// <summary>
/// The last model analysed for each context, with the fingerprint of the compilation it came from. Process-wide, since
/// the driver keeps no state the transform can read, so bounded in two ways: by entries, and by the total number of
/// closure types the held models carry, which is what their memory is proportional to. The least recently used entry
/// goes first, and a model too large for the whole budget is not kept.
/// </summary>
internal static class ModelCache
{
    private const int MaxEntries = 32;

    /// <summary>About four contexts at the closure cap, or many small ones.</summary>
    private const int MaxTypes = 16_384;

    private static readonly object SLock = new();
    private static readonly Dictionary<string, Entry> SEntries = new(StringComparer.Ordinal);
    private static int _types;
    private static long _clock;

    private sealed class Entry(ulong fingerprint, ContextModel model)
    {
        public ulong Fingerprint { get; } = fingerprint;
        public ContextModel Model { get; } = model;
        public long LastUse { get; set; }
    }

    public static bool TryGet(string key, ulong fingerprint, out ContextModel model)
    {
        lock (SLock)
        {
            if (SEntries.TryGetValue(key, out var entry) && entry.Fingerprint == fingerprint)
            {
                entry.LastUse = ++_clock;
                model = entry.Model;
                return true;
            }
        }

        model = null!;
        return false;
    }

    public static void Set(string key, ulong fingerprint, ContextModel model)
    {
        var size = model.Types.Count;
        if (size > MaxTypes)
            return;

        lock (SLock)
        {
            if (SEntries.TryGetValue(key, out var existing))
            {
                SEntries.Remove(key);
                _types -= existing.Model.Types.Count;
            }

            while (SEntries.Count > 0 && (SEntries.Count >= MaxEntries || _types + size > MaxTypes))
                Evict();

            SEntries[key] = new Entry(fingerprint, model) { LastUse = ++_clock };
            _types += size;
        }
    }

    private static void Evict()
    {
        string? oldest = null;
        var oldestUse = long.MaxValue;
        foreach (var pair in SEntries)
        {
            if (pair.Value.LastUse < oldestUse)
            {
                oldestUse = pair.Value.LastUse;
                oldest = pair.Key;
            }
        }

        _types -= SEntries[oldest!].Model.Types.Count;
        SEntries.Remove(oldest!);
    }
}
