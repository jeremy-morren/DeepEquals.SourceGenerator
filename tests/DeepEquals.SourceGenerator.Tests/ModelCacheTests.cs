// Copyright 2026 The DeepEquals source generator project contributors. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using DeepEquals.SourceGenerator.Analysis;
using DeepEquals.SourceGenerator.Model;
using FluentAssertions;
using Xunit;

namespace DeepEquals.SourceGenerator.Tests;

/// <summary>The process-wide model cache holds a bounded number of models and drops the least recently used first.</summary>
public sealed class ModelCacheTests
{
    private static ContextModel Model(string name) =>
        ContextModel.Failed($"ModelCacheTests.{name}", name, null, EquatableArray<DiagnosticInfo>.Empty);

    [Fact]
    public void Entries_are_bounded_and_the_least_recently_used_goes_first()
    {
        const string prefix = "ModelCacheTests|";
        for (var i = 0; i < 40; i++)
        {
            ModelCache.Set(prefix + i, 1, Model("m" + i));
            if (i == 20)
                ModelCache.TryGet(prefix + "0", 1, out _).Should().BeTrue("a read refreshes the entry");
        }

        ModelCache.TryGet(prefix + "0", 1, out _).Should().BeTrue("entry 0 was read after entries 1 to 20 were written, so they went first");
        ModelCache.TryGet(prefix + "1", 1, out _).Should().BeFalse();
        ModelCache.TryGet(prefix + "39", 1, out var latest).Should().BeTrue();
        latest.Name.Should().Be("m39");
        ModelCache.TryGet(prefix + "39", 2, out _).Should().BeFalse("a different fingerprint is a miss");
    }
}
