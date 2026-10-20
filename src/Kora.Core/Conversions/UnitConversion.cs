using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;

namespace Kora.Core.Conversions;

public static partial class UnitConversion
{
    public const int MaximumDigits = 28;
    public const int MaximumFractionDigits = 12;
    public const int ResultDecimalPlaces = 12;
    public const string Precision = "At most 12 decimal places; round to nearest, midpoint to even.";

    private sealed record UnitDefinition(
        ConversionDimension Dimension, long Numerator, int Denominator = 1, int OffsetHundredths = 0);

    // Exact international inch/foot/yard/mile and avoirdupois ounce/pound, not survey or troy units.
    private static readonly Dictionary<ConversionUnit, UnitDefinition> Units =
        new Dictionary<ConversionUnit, UnitDefinition>
        {
            [ConversionUnit.Mm] = new(ConversionDimension.Length, 1000000),
            [ConversionUnit.Cm] = new(ConversionDimension.Length, 10000000),
            [ConversionUnit.M] = new(ConversionDimension.Length, 1000000000),
            [ConversionUnit.Km] = new(ConversionDimension.Length, 1000000000000),
            [ConversionUnit.In] = new(ConversionDimension.Length, 25400000),
            [ConversionUnit.Ft] = new(ConversionDimension.Length, 304800000),
            [ConversionUnit.Yd] = new(ConversionDimension.Length, 914400000),
            [ConversionUnit.Mi] = new(ConversionDimension.Length, 1609344000000),
            [ConversionUnit.Mg] = new(ConversionDimension.Mass, 1000000),
            [ConversionUnit.G] = new(ConversionDimension.Mass, 1000000000),
            [ConversionUnit.Kg] = new(ConversionDimension.Mass, 1000000000000),
            [ConversionUnit.Oz] = new(ConversionDimension.Mass, 28349523125),
            [ConversionUnit.Lb] = new(ConversionDimension.Mass, 453592370000),
            [ConversionUnit.Celsius] = new(ConversionDimension.Temperature, 1, OffsetHundredths: 27315),
            [ConversionUnit.Fahrenheit] = new(ConversionDimension.Temperature, 5, 9, 45967),
            [ConversionUnit.Kelvin] = new(ConversionDimension.Temperature, 1),
        };

    public static ConversionDimension Dimension(ConversionUnit unit) => Units[unit].Dimension;

    public static bool TryParseUnit(string text, out ConversionUnit unit) =>
        Enum.TryParse(text, ignoreCase: true, out unit) && Units.ContainsKey(unit)
        && string.Equals(text, unit.ToString(), StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"^[+-]?[0-9]+(?:\.[0-9]+)?$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex DecimalLiteral();

    public static bool TryParseValue(string text, out decimal value)
    {
        value = 0;
        if (text.Length > MaximumDigits + 2 || !DecimalLiteral().IsMatch(text)) { return false; }
        var point = text.IndexOf('.');
        if (point >= 0 && text.Length - point - 1 > MaximumFractionDigits) { return false; }
        if (text.Count(char.IsAsciiDigit) > MaximumDigits) { return false; }
        return decimal.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture, out value);
    }

    public static ConversionResult Convert(decimal value, ConversionUnit from, ConversionUnit to)
    {
        if (!Units.TryGetValue(from, out var source) || !Units.TryGetValue(to, out var target))
        { return new(ConversionOutcome.UnknownUnit); }
        if (source.Dimension != target.Dimension) { return new(ConversionOutcome.IncompatibleUnits); }
        var bits = decimal.GetBits(value);
        var coefficient = ((BigInteger)(uint)bits[2] << 64) | ((BigInteger)(uint)bits[1] << 32) | (uint)bits[0];
        var scale = (bits[3] >> 16) & 0xff;
        if (scale > MaximumFractionDigits || coefficient >= BigInteger.Pow(10, MaximumDigits))
        { return new(ConversionOutcome.InvalidInput); }
        var inputDenominator = BigInteger.Pow(10, scale);
        var inputNumerator = value < 0 ? -coefficient : coefficient;
        if (value < 0 && source.Dimension != ConversionDimension.Temperature)
        { return new(ConversionOutcome.NegativeQuantity); }

        // Keep exact rational intermediates; decimal multiplication/division would silently lose precision.
        var canonicalNumerator = (inputNumerator * 100 + source.OffsetHundredths * inputDenominator) * source.Numerator;
        var canonicalDenominator = inputDenominator * 100 * source.Denominator;
        if (canonicalNumerator < 0) { return new(ConversionOutcome.BelowAbsoluteZero); }
        var numerator = canonicalNumerator * target.Denominator * 100
            - target.OffsetHundredths * canonicalDenominator * target.Numerator;
        var denominator = canonicalDenominator * target.Numerator * 100;
        if (BigInteger.Abs(numerator) > (BigInteger)decimal.MaxValue * denominator)
        { return new(ConversionOutcome.Overflow); }

        var precision = BigInteger.Pow(10, ResultDecimalPlaces);
        var rounded = BigInteger.DivRem(BigInteger.Abs(numerator) * precision, denominator, out var remainder);
        var midpoint = (remainder * 2).CompareTo(denominator);
        if (midpoint > 0 || midpoint == 0 && !rounded.IsEven) { rounded++; }
        if (rounded.IsZero) { return new(ConversionOutcome.Succeeded, "0", !remainder.IsZero); }
        var whole = BigInteger.DivRem(rounded, precision, out var fraction);
        var fractionText = fraction.ToString("D12", CultureInfo.InvariantCulture).TrimEnd('0');
        var text = whole.ToString(CultureInfo.InvariantCulture);
        if (fractionText.Length > 0) { text += "." + fractionText; }
        if (numerator < 0) { text = "-" + text; }
        return new(ConversionOutcome.Succeeded, text, !remainder.IsZero);
    }
}
