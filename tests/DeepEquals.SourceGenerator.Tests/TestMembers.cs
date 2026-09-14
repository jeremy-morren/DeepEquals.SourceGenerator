// Copyright 2026 The DeepEquals source generator project contributors. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System.Reflection;

namespace DeepEquals.SourceGenerator.Tests;

/// <summary>Writes the storage of the models a test compiles, which are only reachable by reflection.</summary>
internal static class TestMembers
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    /// <summary>Sets a field, public or not; otherwise an auto-property's backing field; otherwise a property's setter.</summary>
    public static void Set(object target, string member, object? value)
    {
        var type = target.GetType();
        var field = type.GetField(member, InstanceMembers) ?? type.GetField($"<{member}>k__BackingField", InstanceMembers);
        if (field is not null)
        {
            field.SetValue(target, value);
            return;
        }

        type.GetProperty(member)!.SetValue(target, value);
    }
}
