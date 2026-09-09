// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System.Collections.Generic;
using DeepEquals.SourceGenerator.Model;
using Microsoft.CodeAnalysis;

namespace DeepEquals.SourceGenerator.Analysis;

/// <summary>The fixed built-in leaf list. Nothing else is promoted; anything not here, not an enum and not [SimpleType] is walked.</summary>
internal static class BuiltInLeaves
{
    internal sealed class Entry
    {
        public Entry(string metadataName, LeafRule rule, bool defaultCompatible, int width = 0, AggregateComponent[]? components = null)
        {
            MetadataName = metadataName;
            Rule = rule;
            DefaultCompatible = defaultCompatible;
            Width = width;
            Components = components ?? [];
        }

        public string MetadataName { get; }

        public LeafRule Rule { get; }

        public bool DefaultCompatible { get; }

        public int Width { get; }

        public AggregateComponent[] Components { get; }
    }

    private static AggregateComponent[] Floats(params string[] names)
    {
        var result = new AggregateComponent[names.Length];
        for (var i = 0; i < names.Length; i++)
            result[i] = new AggregateComponent(names[i], IsDouble: false);

        return result;
    }

    private static AggregateComponent[] Doubles(params string[] names)
    {
        var result = new AggregateComponent[names.Length];
        for (var i = 0; i < names.Length; i++)
            result[i] = new AggregateComponent(names[i], IsDouble: true);

        return result;
    }

    public static readonly Entry[] All =
    [
        new("System.String", LeafRule.String, defaultCompatible: true),
        new("System.Char", LeafRule.Primitive, defaultCompatible: true, width: 2),
        new("System.Boolean", LeafRule.Primitive, defaultCompatible: true, width: 1),
        new("System.Text.Rune", LeafRule.Default, defaultCompatible: true, width: 4),
        new("System.Byte", LeafRule.Primitive, defaultCompatible: true, width: 1),
        new("System.SByte", LeafRule.Primitive, defaultCompatible: true, width: 1),
        new("System.Int16", LeafRule.Primitive, defaultCompatible: true, width: 2),
        new("System.UInt16", LeafRule.Primitive, defaultCompatible: true, width: 2),
        new("System.Int32", LeafRule.Primitive, defaultCompatible: true, width: 4),
        new("System.UInt32", LeafRule.Primitive, defaultCompatible: true, width: 4),
        new("System.Int64", LeafRule.WideInteger, defaultCompatible: true, width: 8),
        new("System.UInt64", LeafRule.WideInteger, defaultCompatible: true, width: 8),
        new("System.IntPtr", LeafRule.WideInteger, defaultCompatible: true, width: 8),
        new("System.UIntPtr", LeafRule.WideInteger, defaultCompatible: true, width: 8),
        new("System.Int128", LeafRule.WideInteger, defaultCompatible: true, width: 16),
        new("System.UInt128", LeafRule.WideInteger, defaultCompatible: true, width: 16),
        new("System.Numerics.BigInteger", LeafRule.Default, defaultCompatible: true, width: 16),
        new("System.Single", LeafRule.Single, defaultCompatible: false, width: 4),
        new("System.Double", LeafRule.Double, defaultCompatible: false, width: 8),
        new("System.Half", LeafRule.Half, defaultCompatible: false, width: 2),
        new("System.Decimal", LeafRule.Decimal, defaultCompatible: false, width: 16),
        new("System.Numerics.Complex", LeafRule.FloatAggregate, defaultCompatible: false, width: 16, Doubles("Real", "Imaginary")),
        new("System.Numerics.Vector2", LeafRule.FloatAggregate, defaultCompatible: false, width: 8, Floats("X", "Y")),
        new("System.Numerics.Vector3", LeafRule.FloatAggregate, defaultCompatible: false, width: 12, Floats("X", "Y", "Z")),
        new("System.Numerics.Vector4", LeafRule.FloatAggregate, defaultCompatible: false, width: 16, Floats("X", "Y", "Z", "W")),
        new("System.Numerics.Quaternion", LeafRule.FloatAggregate, defaultCompatible: false, width: 16, Floats("X", "Y", "Z", "W")),
        new("System.Numerics.Plane", LeafRule.FloatAggregate, defaultCompatible: false, width: 16, Floats("Normal.X", "Normal.Y", "Normal.Z", "D")),
        new("System.Numerics.Matrix3x2", LeafRule.FloatAggregate, defaultCompatible: false, width: 24, Floats("M11", "M12", "M21", "M22", "M31", "M32")),
        new("System.Numerics.Matrix4x4", LeafRule.FloatAggregate, defaultCompatible: false, width: 64, Floats("M11", "M12", "M13", "M14", "M21", "M22", "M23", "M24", "M31", "M32", "M33", "M34", "M41", "M42", "M43", "M44")),
        new("System.Guid", LeafRule.Guid, defaultCompatible: true, width: 16),
        new("System.Version", LeafRule.Default, defaultCompatible: true),
        new("System.Type", LeafRule.Default, defaultCompatible: true),
        new("System.Net.IPAddress", LeafRule.Default, defaultCompatible: true),
        new("System.Globalization.CultureInfo", LeafRule.Default, defaultCompatible: true),
        new("System.TimeZoneInfo", LeafRule.Default, defaultCompatible: true),
        new("System.Text.Encoding", LeafRule.Default, defaultCompatible: true),
        new("System.Uri", LeafRule.Uri, defaultCompatible: false),
        new("System.DateTime", LeafRule.DateTime, defaultCompatible: false, width: 8),
        new("System.DateTimeOffset", LeafRule.DateTimeOffset, defaultCompatible: false, width: 16),
        new("System.TimeSpan", LeafRule.Default, defaultCompatible: true, width: 8),
        new("System.DateOnly", LeafRule.Default, defaultCompatible: true, width: 4),
        new("System.TimeOnly", LeafRule.Default, defaultCompatible: true, width: 8),
        new("System.Index", LeafRule.Default, defaultCompatible: true, width: 4),
        new("System.Range", LeafRule.Default, defaultCompatible: true, width: 8),
        new("System.Drawing.Color", LeafRule.Default, defaultCompatible: true, width: 24),
        new("System.Drawing.Point", LeafRule.Default, defaultCompatible: true, width: 8),
        new("System.Drawing.Size", LeafRule.Default, defaultCompatible: true, width: 8),
        new("System.Drawing.Rectangle", LeafRule.Default, defaultCompatible: true, width: 16),
        new("System.Drawing.PointF", LeafRule.FloatAggregate, defaultCompatible: false, width: 8, Floats("X", "Y")),
        new("System.Drawing.SizeF", LeafRule.FloatAggregate, defaultCompatible: false, width: 8, Floats("Width", "Height")),
        new("System.Drawing.RectangleF", LeafRule.FloatAggregate, defaultCompatible: false, width: 16, Floats("X", "Y", "Width", "Height")),
        new("System.Text.RegularExpressions.Regex", LeafRule.Regex, defaultCompatible: false)
    ];

    private static readonly Dictionary<string, Entry> SByName = Build();

    private static Dictionary<string, Entry> Build()
    {
        var map = new Dictionary<string, Entry>(System.StringComparer.Ordinal);
        foreach (var entry in All)
            map[entry.MetadataName] = entry;

        return map;
    }

    public static Entry? Find(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named || named.IsGenericType)
            return null;

        return SByName.TryGetValue(FullMetadataName(named), out var entry) ? entry : null;
    }

    public static string FullMetadataName(INamedTypeSymbol type)
    {
        if (type.ContainingType is not null)
            return $"{FullMetadataName(type.ContainingType)}+{type.MetadataName}";

        var ns = type.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace 
            ? type.MetadataName 
            : $"{ns.ToDisplayString()}.{type.MetadataName}";
    }
}
