using System.Globalization;
using AwesomeAssertions;
using Kora.Core.Conversions;

namespace Kora.Core.UnitTests.Conversions;

public sealed class UnitConversionTests
{
    [Fact]
    public void EveryPairAndIdentityHasAnIndependentKnownFactorOrOffset()
    {
        (ConversionUnit Unit, decimal Factor)[] lengths =
        [
            (ConversionUnit.Mm, .001m), (ConversionUnit.Cm, .01m), (ConversionUnit.M, 1m),
            (ConversionUnit.Km, 1000m), (ConversionUnit.In, .0254m), (ConversionUnit.Ft, .3048m),
            (ConversionUnit.Yd, .9144m), (ConversionUnit.Mi, 1609.344m),
        ];
        (ConversionUnit Unit, decimal Factor)[] masses =
        [
            (ConversionUnit.Mg, .000001m), (ConversionUnit.G, .001m), (ConversionUnit.Kg, 1m),
            (ConversionUnit.Oz, .028349523125m), (ConversionUnit.Lb, .45359237m),
        ];
        foreach (var group in new[] { lengths, masses })
        foreach (var from in group)
        foreach (var to in group)
        {
            UnitConversion.Dimension(from.Unit).Should().Be(UnitConversion.Dimension(to.Unit));
            var result = UnitConversion.Convert(1, from.Unit, to.Unit);
            result.Outcome.Should().Be(ConversionOutcome.Succeeded);
            decimal.Parse(result.Value!, CultureInfo.InvariantCulture).Should().Be(
                decimal.Round(from.Factor / to.Factor, 12, MidpointRounding.ToEven));
            if (from == to) { result.Should().Be(new ConversionResult(ConversionOutcome.Succeeded, "1")); }
        }
        (ConversionUnit Unit, decimal Value)[] freezing =
        [(ConversionUnit.Celsius, 0), (ConversionUnit.Fahrenheit, 32), (ConversionUnit.Kelvin, 273.15m)];
        foreach (var from in freezing)
        foreach (var to in freezing)
        {
            UnitConversion.Dimension(from.Unit).Should().Be(ConversionDimension.Temperature);
            UnitConversion.Convert(from.Value, from.Unit, to.Unit).Should().Be(
                new ConversionResult(ConversionOutcome.Succeeded, to.Value.ToString(CultureInfo.InvariantCulture)));
        }
    }

    [Theory]
    [InlineData("1", ConversionUnit.In, ConversionUnit.M, "0.0254", false)]
    [InlineData("1", ConversionUnit.Lb, ConversionUnit.Oz, "16", false)]
    [InlineData("1", ConversionUnit.Oz, ConversionUnit.Kg, "0.028349523125", false)]
    [InlineData("1", ConversionUnit.M, ConversionUnit.In, "39.370078740157", true)]
    [InlineData("0", ConversionUnit.Fahrenheit, ConversionUnit.Celsius, "-17.777777777778", true)]
    [InlineData("-40", ConversionUnit.Celsius, ConversionUnit.Fahrenheit, "-40", false)]
    [InlineData("100", ConversionUnit.Celsius, ConversionUnit.Fahrenheit, "212", false)]
    [InlineData("-273.15", ConversionUnit.Celsius, ConversionUnit.Kelvin, "0", false)]
    [InlineData("-459.67", ConversionUnit.Fahrenheit, ConversionUnit.Kelvin, "0", false)]
    [InlineData("0", ConversionUnit.Kelvin, ConversionUnit.Celsius, "-273.15", false)]
    [InlineData("0.000000000001", ConversionUnit.Mm, ConversionUnit.M, "0", true)]
    [InlineData("0.0000000005", ConversionUnit.Mm, ConversionUnit.M, "0", true)]
    [InlineData("0.0000000015", ConversionUnit.Mm, ConversionUnit.M, "0.000000000002", true)]
    [InlineData("0.0000000025", ConversionUnit.Mm, ConversionUnit.M, "0.000000000002", true)]
    [InlineData("9999999999999999999999999999", ConversionUnit.Mm, ConversionUnit.Mm, "9999999999999999999999999999", false)]
    [InlineData("1234567890123456.123456789012", ConversionUnit.Kg, ConversionUnit.Kg, "1234567890123456.123456789012", false)]
    public void ExactLimitsOffsetsAndMidpointToEvenAreExplicit(string value, ConversionUnit from, ConversionUnit to, string expected, bool rounded) =>
        UnitConversion.Convert(decimal.Parse(value, CultureInfo.InvariantCulture), from, to).Should().Be(
            new ConversionResult(ConversionOutcome.Succeeded, expected, rounded));

    [Theory]
    [InlineData("-1", ConversionUnit.M, ConversionUnit.M, ConversionOutcome.NegativeQuantity)]
    [InlineData("-1", ConversionUnit.Kg, ConversionUnit.Mg, ConversionOutcome.NegativeQuantity)]
    [InlineData("-273.150000000001", ConversionUnit.Celsius, ConversionUnit.Celsius, ConversionOutcome.BelowAbsoluteZero)]
    [InlineData("-459.670000000001", ConversionUnit.Fahrenheit, ConversionUnit.Celsius, ConversionOutcome.BelowAbsoluteZero)]
    [InlineData("-0.000000000001", ConversionUnit.Kelvin, ConversionUnit.Kelvin, ConversionOutcome.BelowAbsoluteZero)]
    [InlineData("1", ConversionUnit.M, ConversionUnit.Kg, ConversionOutcome.IncompatibleUnits)]
    [InlineData("1", (ConversionUnit)99, ConversionUnit.M, ConversionOutcome.UnknownUnit)]
    [InlineData("1", ConversionUnit.M, (ConversionUnit)99, ConversionOutcome.UnknownUnit)]
    [InlineData("9999999999999999999999999999", ConversionUnit.Mi, ConversionUnit.Mm, ConversionOutcome.Overflow)]
    [InlineData("0.0000000000001", ConversionUnit.M, ConversionUnit.M, ConversionOutcome.InvalidInput)]
    [InlineData("10000000000000000000000000000", ConversionUnit.M, ConversionUnit.M, ConversionOutcome.InvalidInput)]
    public void InvalidTypedRequestsNeverReturnAValue(string value, ConversionUnit from, ConversionUnit to, ConversionOutcome outcome) =>
        UnitConversion.Convert(decimal.Parse(value, CultureInfo.InvariantCulture), from, to)
            .Should().Be(new ConversionResult(outcome));

    [Theory]
    [InlineData("convert +1.25 M to CM", "125")]
    [InlineData("convert -0 m to mm", "0")]
    [InlineData("convert 0001 m to m", "1")]
    public void GrammarAndResultsAreCultureIndependent(string text, string expected)
    {
        var prior = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var command = ConvertCommand.Parse(text, "Kora")!;
            command.Value.Should().NotBeNull();
            UnitConversion.Convert(command.Value!.Value, command.From!.Value, command.To!.Value).Value.Should().Be(expected);
        }
        finally { CultureInfo.CurrentCulture = prior; }
    }

    [Theory]
    [InlineData("")]
    [InlineData("ask a model")]
    [InlineData("oldname convert 1 m to cm")]
    public void UnreservedInputDoesNotMatch(string text) => ConvertCommand.Parse(text, "Kora").Should().BeNull();

    [Fact]
    public void HelpAndCurrentNameActivationAreExact()
    {
        ConvertCommand.Parse("  Nova, CONVERT HELP  ", "Nova").Should().Be(new ConvertCommand(true, IsHelp: true));
        ConvertCommand.Parse("convert help", "Nova")!.IsActivated.Should().BeFalse();
        ConvertCommand.Parse("Nóva, convert help", "Nóva")!.IsHelp.Should().BeTrue();
        ConvertCommand.Parse("convert 1 xyz to cm", "Kora")!.Error.Should().Be(ConversionOutcome.UnknownUnit);
        ConvertCommand.Parse("convert 1 m to 1", "Kora")!.Error.Should().Be(ConversionOutcome.UnknownUnit);
        ConvertCommand.Parse("convert 1 999 to m", "Kora")!.Error.Should().Be(ConversionOutcome.UnknownUnit);
        ConvertCommand.Parse("convert " + new string('1', 155), "Kora")!.Value.Should().BeNull();
        UnitConversion.TryParseValue(new string('1', 31), out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("convert")]
    [InlineData("convert help please")]
    [InlineData("convertible")]
    [InlineData("convertible 1 m to cm")]
    [InlineData("convert 1 m into cm")]
    [InlineData("convert 1 m to cm please")]
    [InlineData("convert  1 m to cm")]
    [InlineData("convert 1e2 m to cm")]
    [InlineData("convert .5 m to cm")]
    [InlineData("convert 5. m to cm")]
    [InlineData("convert 1,5 m to cm")]
    [InlineData("convert 1+2 m to cm")]
    [InlineData("convert １ m to cm")]
    [InlineData("convert −1 m to cm")]
    [InlineData("convert 1\u00a0m to cm")]
    [InlineData("convert 1\tm to cm")]
    [InlineData("convert 1 m to cm\n")]
    [InlineData("convert 10000000000000000000000000000 m to cm")]
    [InlineData("convert 0.0000000000001 m to cm")]
    public void MalformedReservedInputCannotFallThrough(string text)
    {
        var command = ConvertCommand.Parse(text, "Kora");
        command.Should().NotBeNull();
        command!.Value.Should().BeNull();
        command.Error.Should().Be(ConversionOutcome.InvalidInput);
    }
}
