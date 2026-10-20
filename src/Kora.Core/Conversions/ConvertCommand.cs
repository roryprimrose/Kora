using Kora.Core.Artifacts;

namespace Kora.Core.Conversions;

public sealed record ConvertCommand(bool IsActivated, bool IsHelp = false, decimal? Value = null,
    ConversionUnit? From = null, ConversionUnit? To = null, ConversionOutcome Error = ConversionOutcome.InvalidInput)
{
    public const string Syntax = "Use convert <decimal> <unit> to <unit> or convert help. "
        + "Length: mm, cm, m, km, in, ft, yd, mi. Mass: mg, g, kg, oz, lb. "
        + "Temperature: celsius, fahrenheit, kelvin. IDs are case-insensitive. "
        + "Signed invariant decimals only: at most 28 digits total, 12 fractional digits; "
        + "no exponent, separators, expressions or aliases. " + UnitConversion.Precision;

    public static ConvertCommand? Parse(string input, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(input)) { return null; }
        var text = input.Trim();
        var body = ArtifactCommandRouter.RemoveActivationPrefix(text, assistantName);
        if (!body.StartsWith("convert", StringComparison.OrdinalIgnoreCase)) { return null; }
        var activated = !string.Equals(text, body, StringComparison.Ordinal);
        if (input.Length > 160 || input.Any(char.IsControl) || body.Any(character => character > 127))
        { return new(activated); }
        if (string.Equals(body, "convert help", StringComparison.OrdinalIgnoreCase)) { return new(activated, IsHelp: true); }
        var tokens = body.Split(' ');
        if (tokens.Length != 5 || !string.Equals(tokens[0], "convert", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(tokens[3], "to", StringComparison.OrdinalIgnoreCase)
            || !UnitConversion.TryParseValue(tokens[1], out var value))
        { return new(activated); }
        if (!UnitConversion.TryParseUnit(tokens[2], out var from) || !UnitConversion.TryParseUnit(tokens[4], out var to))
        { return new(activated, Error: ConversionOutcome.UnknownUnit); }
        return new(activated, Value: value, From: from, To: to);
    }
}
