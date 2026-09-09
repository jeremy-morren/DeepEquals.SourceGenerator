// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;

namespace DeepEquals.SourceGeneration.Framework;

/// <summary>A field read through a by-ref receiver, so a struct receiver is not copied once per member read.</summary>
public delegate TField FieldGetter<TDecl, out TField>(ref TDecl receiver);

/// <summary>The delegate fallback for field access where <c>UnsafeAccessor</c> is unavailable.</summary>
public static class DeepEqualsReflection
{
    /// <summary>
    /// Builds a getter for the instance field <paramref name="fieldName"/> declared on <paramref name="declaringType"/> with an
    /// expression-tree lambda whose receiver parameter is by-ref. Called once per field from a lazy holder.
    /// </summary>
    [RequiresUnreferencedCode("Reads a private field by name; trimming can remove the field.")]
    [RequiresDynamicCode("Compiles an expression tree; not available under NativeAOT.")]
    public static FieldGetter<TDecl, TField> CreateFieldGetter<TDecl, TField>(Type declaringType, string fieldName)
    {
#if NET6_0_OR_GREATER
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentNullException.ThrowIfNull(fieldName);
#else
        if (declaringType is null) throw new ArgumentNullException(nameof(declaringType));
        if (fieldName is null) throw new ArgumentNullException(nameof(fieldName));
#endif

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        var field = declaringType.GetField(fieldName, flags)
                    ?? throw new MissingFieldException(declaringType.FullName, fieldName);

        var receiver = Expression.Parameter(typeof(TDecl).MakeByRefType(), "receiver");
        Expression body = Expression.Field(receiver, field);
        if (body.Type != typeof(TField))
            body = Expression.Convert(body, typeof(TField));

        return Expression.Lambda<FieldGetter<TDecl, TField>>(body, receiver).Compile();
    }
}
