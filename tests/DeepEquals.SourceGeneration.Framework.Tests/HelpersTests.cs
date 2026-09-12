// Copyright 2026 Jeremy Morren <jeremy.morren@outlook.com>. All rights reserved.
// Source code is available at https://github.com/jeremy-morren/DeepEquals.SourceGenerator
// Use of this source code is governed by the MIT License as found in the LICENSE.txt file

using System;
using System.Linq;
using FluentAssertions;
using Xunit;

namespace DeepEquals.SourceGeneration.Framework.Tests;

/// <summary>The layout-dependent helpers must agree with the documented API they replace on every supported runtime.</summary>
public sealed class HelpersTests
{
    [Theory]
    [InlineData("1.0", "1.0", true)]
    [InlineData("1.0", "1.00", false)]
    [InlineData("0", "0.0", false)]
    [InlineData("79228162514264337593543950335", "79228162514264337593543950335", true)]
    [InlineData("-79228162514264337593543950335", "79228162514264337593543950335", false)]
    [InlineData("0.0000000000000000000000000001", "0.0000000000000000000000000001", true)]
    [InlineData("123.456", "123.457", false)]
    public void DecimalEquals_is_the_GetBits_comparison(string left, string right, bool expected)
    {
        var x = decimal.Parse(left, System.Globalization.CultureInfo.InvariantCulture);
        var y = decimal.Parse(right, System.Globalization.CultureInfo.InvariantCulture);
        var bits = decimal.GetBits(x).SequenceEqual(decimal.GetBits(y));

        DeepEqualsHelpers.DecimalEquals(x, y).Should().Be(bits);
        DeepEqualsHelpers.DecimalEquals(x, y).Should().Be(expected);
        DeepEqualsHelpers.DecimalEquals(y, x).Should().Be(expected);
    }

    [Fact]
    public void Negative_zero_is_a_different_decimal()
    {
        var negativeZero = decimal.Negate(0m);
        decimal.GetBits(negativeZero).SequenceEqual(decimal.GetBits(0m)).Should().BeFalse("the sign bit is set");
        DeepEqualsHelpers.DecimalEquals(negativeZero, 0m).Should().BeFalse();
        DeepEqualsHashCode.Hash(negativeZero).Should().NotBe(DeepEqualsHashCode.Hash(0m));
    }

    [Fact]
    public void Decimal_words_cover_every_GetBits_word_once()
    {
        var values = new[] { 0m, 1m, -1m, 1.5m, 1.50m, decimal.MaxValue, decimal.MinValue, 0.0000000000000000000000000001m, 1234567890123456789012345678m };
        foreach (var value in values)
        {
            var words = new[]
            {
                DeepEqualsHelpers.DecimalWord(value, 0),
                DeepEqualsHelpers.DecimalWord(value, 1),
                DeepEqualsHelpers.DecimalWord(value, 2),
                DeepEqualsHelpers.DecimalWord(value, 3),
            };

            // The runtime lays the words out in its own order; the set of words is what GetBits returns.
            words.Should().BeEquivalentTo(decimal.GetBits(value), $"the storage of {value} is its four GetBits words");
            DeepEqualsHashCode.Hash(value).Should().Be(DeepEqualsHashCode.Combine(words[0], words[1], words[2], words[3]));
        }

        DeepEqualsHashCode.Hash(1.5m).Should().NotBe(DeepEqualsHashCode.Hash(1.50m), "the scale is part of the value");
    }

    public static TheoryData<float> Floats => new() { 1.5f, float.MaxValue, float.Epsilon, 0f, -0f, float.NaN, BitConverter.ToSingle(BitConverter.GetBytes(0x7FC00001), 0), float.NegativeInfinity };

    public static TheoryData<double> Doubles => new() { 1.5, double.MaxValue, double.Epsilon, 0.0, -0.0, double.NaN, BitConverter.ToDouble(BitConverter.GetBytes(0x7FF8000000000001L), 0), double.NegativeInfinity };

    [Theory]
    [MemberData(nameof(Floats))]
    public void FloatBits_returns_the_storage_bytes(float value)
    {
        BitConverter.GetBytes(DeepEqualsHelpers.FloatBits(value)).Should().Equal(BitConverter.GetBytes(value));
    }

    [Theory]
    [MemberData(nameof(Doubles))]
    public void DoubleBits_returns_the_storage_bytes(double value)
    {
        BitConverter.GetBytes(DeepEqualsHelpers.DoubleBits(value)).Should().Equal(BitConverter.GetBytes(value));
    }

    [Fact]
    public void Bit_shims_keep_negative_zero_and_nan_payloads_apart()
    {
        DeepEqualsHelpers.FloatBits(-0f).Should().NotBe(DeepEqualsHelpers.FloatBits(0f));
        DeepEqualsHelpers.DoubleBits(-0.0).Should().NotBe(DeepEqualsHelpers.DoubleBits(0.0));
        DeepEqualsHelpers.FloatBits(float.NaN).Should().NotBe(DeepEqualsHelpers.FloatBits(BitConverter.ToSingle(BitConverter.GetBytes(0x7FC00001), 0)));
        DeepEqualsHelpers.DoubleBits(double.NaN).Should().NotBe(DeepEqualsHelpers.DoubleBits(BitConverter.ToDouble(BitConverter.GetBytes(0x7FF8000000000001L), 0)));
        DeepEqualsHelpers.FloatBits(float.NaN).Should().Be(DeepEqualsHelpers.FloatBits(float.NaN), "the same payload is the same bits");
    }

#if NET8_0_OR_GREATER
    [Fact]
    public void NullableValueRef_reaches_the_payload_in_place()
    {
        decimal? value = 1.50m;
        ref readonly var payload = ref DeepEqualsHelpers.NullableValueRef(in value);
        payload.Should().Be(1.50m);
        value = 2m;
        payload.Should().Be(2m, "the reference is into the nullable itself, not a copy");
        decimal.GetBits(payload).Should().Equal(decimal.GetBits(2m));

        decimal? none = null;
        DeepEqualsHelpers.NullableValueRef(none).Should().Be(0m, "an empty nullable yields its default payload, as GetValueOrDefault() does");
    }
#endif

#if NET6_0_OR_GREATER
    [Fact]
    public void HalfBits_returns_the_storage_bytes()
    {
        foreach (var value in new[] { (Half)1.5, Half.MaxValue, Half.Epsilon, (Half)0, (Half)(-0.0), Half.NaN })
            BitConverter.GetBytes(DeepEqualsHelpers.HalfBits(value)).Should().Equal(BitConverter.GetBytes(value));

        DeepEqualsHelpers.HalfBits((Half)(-0.0)).Should().NotBe(DeepEqualsHelpers.HalfBits((Half)0));
    }
#endif

    [Fact]
    public void Decimal_words_are_its_storage()
    {
        var values = new[] { 0m, 1m, -1m, 1.5m, 1.50m, decimal.MaxValue, decimal.MinValue, 0.0000000000000000000000000001m, decimal.Negate(0m) };
        foreach (var value in values)
        {
            // Storage order is not GetBits order; the set of words is what GetBits returns.
            var words = Enumerable.Range(0, 4).Select(i => DeepEqualsHelpers.DecimalWord(value, i)).ToArray();
            words.Should().BeEquivalentTo(decimal.GetBits(value));
        }
    }

    [Fact]
    public void Guid_words_are_its_sixteen_bytes()
    {
        var guid = new Guid("00112233-4455-6677-8899-aabbccddeeff");
        var words = new[]
        {
            DeepEqualsHelpers.GuidWord(guid, 0),
            DeepEqualsHelpers.GuidWord(guid, 1),
            DeepEqualsHelpers.GuidWord(guid, 2),
            DeepEqualsHelpers.GuidWord(guid, 3),
        };

        var bytes = new byte[16];
        Buffer.BlockCopy(words, 0, bytes, 0, 16);
        bytes.Should().Equal(guid.ToByteArray());
        DeepEqualsHashCode.Hash(guid).Should().Be(DeepEqualsHashCode.Combine(words[0], words[1], words[2], words[3]));

        var last = new Guid("00112233-4455-6677-8899-aabbccddee00");
        DeepEqualsHelpers.GuidWord(last, 3).Should().NotBe(words[3], "the last byte lives in the last word");
    }
}
