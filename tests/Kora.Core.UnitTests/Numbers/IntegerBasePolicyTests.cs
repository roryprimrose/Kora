using System.Globalization;
using AwesomeAssertions;
using Kora.Core.Numbers;

namespace Kora.Core.UnitTests.Numbers;

public sealed class IntegerBasePolicyTests
{
    public static TheoryData<IntegerBase, IntegerBase, string, string> Pairs()
    {
        var data = new TheoryData<IntegerBase, IntegerBase, string, string>();
        string[][] representations =
        [
            ["0", "0", "0", "0"],
            ["1", "1", "1", "1"],
            ["-1", "-1", "-1", "-1"],
            ["101010", "52", "42", "2a"],
            ["-101010", "-52", "-42", "-2a"],
            [new('1', 63), "777777777777777777777", "9223372036854775807", "7fffffffffffffff"],
            ["-1" + new string('0', 63), "-1000000000000000000000", "-9223372036854775808", "-8000000000000000"],
        ];
        var bases = Enum.GetValues<IntegerBase>();
        foreach (var values in representations)
        {
            for (var from = 0; from < bases.Length; from++)
            {
                for (var to = 0; to < bases.Length; to++)
                {
                    data.Add(bases[from], bases[to], values[from], values[to]);
                }
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(Pairs))]
    public void AllSixteenPairsIncludingIdentityAndBoundsRoundTrip(
        IntegerBase from, IntegerBase to, string source, string expected)
    {
        var result = IntegerBasePolicy.Convert(new(source, from, to));
        result.Should().Be(new IntegerConversionResult(IntegerConversionOutcome.Succeeded, expected));
        result.Representation!.Length.Should().BeLessThanOrEqualTo(IntegerBasePolicy.MaximumRepresentationBytes);
        IntegerBasePolicy.Convert(new(result.Representation, to, from)).Representation.Should().Be(source);
    }

    [Theory]
    [InlineData("", IntegerBase.BaseTen)]
    [InlineData("-", IntegerBase.BaseTen)]
    [InlineData("+1", IntegerBase.BaseTen)]
    [InlineData("-0", IntegerBase.BaseTen)]
    [InlineData("00", IntegerBase.BaseTen)]
    [InlineData("-01", IntegerBase.Hex)]
    [InlineData("01", IntegerBase.Binary)]
    [InlineData("0x1", IntegerBase.Hex)]
    [InlineData("0b1", IntegerBase.Binary)]
    [InlineData("2", IntegerBase.Binary)]
    [InlineData("8", IntegerBase.Octal)]
    [InlineData("a", IntegerBase.BaseTen)]
    [InlineData("g", IntegerBase.Hex)]
    [InlineData(" 1", IntegerBase.BaseTen)]
    [InlineData("1 ", IntegerBase.BaseTen)]
    [InlineData("1_000", IntegerBase.BaseTen)]
    [InlineData("1,000", IntegerBase.BaseTen)]
    [InlineData("1.0", IntegerBase.BaseTen)]
    [InlineData("1e2", IntegerBase.BaseTen)]
    [InlineData("1+2", IntegerBase.BaseTen)]
    [InlineData("−1", IntegerBase.BaseTen)]
    [InlineData("１", IntegerBase.BaseTen)]
    [InlineData("١", IntegerBase.BaseTen)]
    [InlineData("Ｆ", IntegerBase.Hex)]
    [InlineData("1\0", IntegerBase.Binary)]
    [InlineData("922337203685477580899z", IntegerBase.BaseTen)]
    public void RejectsNoncanonicalOrInvalidCompleteLiterals(string literal, IntegerBase from) =>
        IntegerBasePolicy.Convert(new(literal, from, IntegerBase.BaseTen)).Should()
            .Be(new IntegerConversionResult(IntegerConversionOutcome.InvalidInteger));

    [Theory]
    [InlineData("9223372036854775808", IntegerBase.BaseTen)]
    [InlineData("-9223372036854775809", IntegerBase.BaseTen)]
    [InlineData("8000000000000000", IntegerBase.Hex)]
    [InlineData("-8000000000000001", IntegerBase.Hex)]
    [InlineData("1000000000000000000000", IntegerBase.Octal)]
    [InlineData("-1000000000000000000001", IntegerBase.Octal)]
    public void RejectsSignedRangeOverflow(string literal, IntegerBase from) =>
        IntegerBasePolicy.Convert(new(literal, from, IntegerBase.Binary)).Outcome.Should().Be(IntegerConversionOutcome.Overflow);

    [Fact]
    public void EnforcesLiteralAndBaseBounds()
    {
        IntegerBasePolicy.Convert(new(new string('1', 66), IntegerBase.Binary, IntegerBase.Hex)).Outcome.Should().Be(IntegerConversionOutcome.InvalidInteger);
        IntegerBasePolicy.Convert(new(new string('1', 64), IntegerBase.Binary, IntegerBase.Hex)).Outcome.Should().Be(IntegerConversionOutcome.Overflow);
        IntegerBasePolicy.Convert(new("-" + new string('1', 64), IntegerBase.Binary, IntegerBase.Hex)).Outcome.Should().Be(IntegerConversionOutcome.Overflow);
        IntegerBasePolicy.Convert(new("1", (IntegerBase)3, IntegerBase.Hex)).Outcome.Should().Be(IntegerConversionOutcome.InvalidBase);
        IntegerBasePolicy.Convert(new("1", IntegerBase.Hex, (IntegerBase)0)).Outcome.Should().Be(IntegerConversionOutcome.InvalidBase);
        var act = () => IntegerBasePolicy.Convert(null!);
        act.Should().Throw<ArgumentNullException>();
        IntegerBasePolicy.Convert(new(null!, IntegerBase.Binary, IntegerBase.Hex)).Outcome.Should().Be(IntegerConversionOutcome.InvalidInteger);
    }

    [Theory]
    [InlineData("tr-TR")]
    [InlineData("ar-SA")]
    [InlineData("fr-FR")]
    public void HexIsCaseInsensitiveAndOutputIsCanonicalInEveryCulture(string culture)
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            IntegerBasePolicy.Convert(new("-AbCdEF", IntegerBase.Hex, IntegerBase.Hex)).Representation.Should().Be("-abcdef");
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
