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
            Components = components ?? System.Array.Empty<AggregateComponent>();
        }

        public string MetadataName { get; }

        public LeafRule Rule { get; }

        public bool DefaultCompatible { get; }

        public int Width { get; }

        public AggregateComponent[] Components { get; }
    }

    private static AggregateComponent[] Floats(params string[] names)
    {
        AggregateComponent[] result = new AggregateComponent[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            result[i] = new AggregateComponent(names[i], IsDouble: false);
        }

        return result;
    }

    private static AggregateComponent[] Doubles(params string[] names)
    {
        AggregateComponent[] result = new AggregateComponent[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            result[i] = new AggregateComponent(names[i], IsDouble: true);
        }

        return result;
    }

    public static readonly Entry[] All =
    {
        new Entry("System.String", LeafRule.String, defaultCompatible: true),
        new Entry("System.Char", LeafRule.Primitive, defaultCompatible: true, width: 2),
        new Entry("System.Boolean", LeafRule.Primitive, defaultCompatible: true, width: 1),
        new Entry("System.Text.Rune", LeafRule.Default, defaultCompatible: true, width: 4),
        new Entry("System.Byte", LeafRule.Primitive, defaultCompatible: true, width: 1),
        new Entry("System.SByte", LeafRule.Primitive, defaultCompatible: true, width: 1),
        new Entry("System.Int16", LeafRule.Primitive, defaultCompatible: true, width: 2),
        new Entry("System.UInt16", LeafRule.Primitive, defaultCompatible: true, width: 2),
        new Entry("System.Int32", LeafRule.Primitive, defaultCompatible: true, width: 4),
        new Entry("System.UInt32", LeafRule.Primitive, defaultCompatible: true, width: 4),
        new Entry("System.Int64", LeafRule.WideInteger, defaultCompatible: true, width: 8),
        new Entry("System.UInt64", LeafRule.WideInteger, defaultCompatible: true, width: 8),
        new Entry("System.IntPtr", LeafRule.WideInteger, defaultCompatible: true, width: 8),
        new Entry("System.UIntPtr", LeafRule.WideInteger, defaultCompatible: true, width: 8),
        new Entry("System.Int128", LeafRule.WideInteger, defaultCompatible: true, width: 16),
        new Entry("System.UInt128", LeafRule.WideInteger, defaultCompatible: true, width: 16),
        new Entry("System.Numerics.BigInteger", LeafRule.Default, defaultCompatible: true, width: 16),
        new Entry("System.Single", LeafRule.Single, defaultCompatible: false, width: 4),
        new Entry("System.Double", LeafRule.Double, defaultCompatible: false, width: 8),
        new Entry("System.Half", LeafRule.Half, defaultCompatible: false, width: 2),
        new Entry("System.Decimal", LeafRule.Decimal, defaultCompatible: false, width: 16),
        new Entry("System.Numerics.Complex", LeafRule.FloatAggregate, defaultCompatible: false, width: 16, Doubles("Real", "Imaginary")),
        new Entry("System.Numerics.Vector2", LeafRule.FloatAggregate, defaultCompatible: false, width: 8, Floats("X", "Y")),
        new Entry("System.Numerics.Vector3", LeafRule.FloatAggregate, defaultCompatible: false, width: 12, Floats("X", "Y", "Z")),
        new Entry("System.Numerics.Vector4", LeafRule.FloatAggregate, defaultCompatible: false, width: 16, Floats("X", "Y", "Z", "W")),
        new Entry("System.Numerics.Quaternion", LeafRule.FloatAggregate, defaultCompatible: false, width: 16, Floats("X", "Y", "Z", "W")),
        new Entry("System.Numerics.Plane", LeafRule.FloatAggregate, defaultCompatible: false, width: 16, Floats("Normal.X", "Normal.Y", "Normal.Z", "D")),
        new Entry("System.Numerics.Matrix3x2", LeafRule.FloatAggregate, defaultCompatible: false, width: 24, Floats("M11", "M12", "M21", "M22", "M31", "M32")),
        new Entry("System.Numerics.Matrix4x4", LeafRule.FloatAggregate, defaultCompatible: false, width: 64, Floats("M11", "M12", "M13", "M14", "M21", "M22", "M23", "M24", "M31", "M32", "M33", "M34", "M41", "M42", "M43", "M44")),
        new Entry("System.Guid", LeafRule.Guid, defaultCompatible: true, width: 16),
        new Entry("System.Version", LeafRule.Default, defaultCompatible: true),
        new Entry("System.Type", LeafRule.Default, defaultCompatible: true),
        new Entry("System.Net.IPAddress", LeafRule.Default, defaultCompatible: true),
        new Entry("System.Globalization.CultureInfo", LeafRule.Default, defaultCompatible: true),
        new Entry("System.TimeZoneInfo", LeafRule.Default, defaultCompatible: true),
        new Entry("System.Text.Encoding", LeafRule.Default, defaultCompatible: true),
        new Entry("System.Uri", LeafRule.Uri, defaultCompatible: false),
        new Entry("System.DateTime", LeafRule.DateTime, defaultCompatible: false, width: 8),
        new Entry("System.DateTimeOffset", LeafRule.DateTimeOffset, defaultCompatible: false, width: 16),
        new Entry("System.TimeSpan", LeafRule.Default, defaultCompatible: true, width: 8),
        new Entry("System.DateOnly", LeafRule.Default, defaultCompatible: true, width: 4),
        new Entry("System.TimeOnly", LeafRule.Default, defaultCompatible: true, width: 8),
        new Entry("System.Index", LeafRule.Default, defaultCompatible: true, width: 4),
        new Entry("System.Range", LeafRule.Default, defaultCompatible: true, width: 8),
        new Entry("System.Drawing.Color", LeafRule.Default, defaultCompatible: true, width: 24),
        new Entry("System.Drawing.Point", LeafRule.Default, defaultCompatible: true, width: 8),
        new Entry("System.Drawing.Size", LeafRule.Default, defaultCompatible: true, width: 8),
        new Entry("System.Drawing.Rectangle", LeafRule.Default, defaultCompatible: true, width: 16),
        new Entry("System.Drawing.PointF", LeafRule.FloatAggregate, defaultCompatible: false, width: 8, Floats("X", "Y")),
        new Entry("System.Drawing.SizeF", LeafRule.FloatAggregate, defaultCompatible: false, width: 8, Floats("Width", "Height")),
        new Entry("System.Drawing.RectangleF", LeafRule.FloatAggregate, defaultCompatible: false, width: 16, Floats("X", "Y", "Width", "Height")),
        new Entry("System.Text.RegularExpressions.Regex", LeafRule.Regex, defaultCompatible: false),
    };

    private static readonly Dictionary<string, Entry> s_byName = Build();

    private static Dictionary<string, Entry> Build()
    {
        Dictionary<string, Entry> map = new Dictionary<string, Entry>(System.StringComparer.Ordinal);
        foreach (Entry entry in All)
        {
            map[entry.MetadataName] = entry;
        }

        return map;
    }

    public static Entry? Find(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named || named.IsGenericType)
        {
            return null;
        }

        return s_byName.TryGetValue(FullMetadataName(named), out Entry? entry) ? entry : null;
    }

    public static string FullMetadataName(INamedTypeSymbol type)
    {
        if (type.ContainingType is not null)
        {
            return FullMetadataName(type.ContainingType) + "+" + type.MetadataName;
        }

        INamespaceSymbol? ns = type.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace ? type.MetadataName : ns.ToDisplayString() + "." + type.MetadataName;
    }
}
