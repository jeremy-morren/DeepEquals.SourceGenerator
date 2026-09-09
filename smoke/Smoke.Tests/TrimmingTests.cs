// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System.Linq;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace DeepEquals.Smoke.Tests;

/// <summary>
/// The trimming warning boundary.
/// A consumer that only touches comparers reachable without reflection must publish clean;
/// a consumer that touches one that needs a delegate must be warned, at its own line rather than inside a generated file.
/// The design's "toolchain warnings follow the unsafe path" section is what these two assert.
/// </summary>
[Trait("Category", "Trimming")]
public sealed class TrimmingTests
{
    private readonly ITestOutputHelper _output;

    public TrimmingTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void A_consumer_on_the_safe_path_publishes_without_a_trimming_warning()
    {
        // net10.0: both registered types take the UnsafeAccessor path, and this consumer touches only the plain comparer.
        var publish = Dotnet.Publish(_output, SmokePaths.Consumer("Trimming.Safe"));

        publish.ExitCode.Should().Be(0, "the safe consumer must publish");
        publish.TrimmingWarnings.Should().BeEmpty("nothing on the safe path may warn");
    }

    [Fact]
    public void A_consumer_on_the_unsafe_path_is_warned_at_its_own_line()
    {
        // net8.0: the generic holder takes the delegate path, so touching its comparer is unsafe.
        var publish = Dotnet.Publish(_output, SmokePaths.Consumer("Trimming.Unsafe"));

        publish.ExitCode.Should().Be(0, "warnings are not errors for this consumer");

        var warnings = publish.TrimmingWarnings.ToList();

        warnings.Should().NotBeEmpty("the unsafe access must be reported");
        warnings.Should().OnlyContain(
            warning => warning.Contains("Program.cs"),
            "a warning must land on the consumer's own access, never inside a generated file");
        warnings.Should().NotContain(
            warning => warning.Contains(".g.cs"),
            "a generated file must never be the reported location");
    }
}
