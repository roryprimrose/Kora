using System.Text;
using Kora.Core.Configuration;
using Kora.Core.Numbers;

namespace Kora.Application.Numbers;

/// <summary>Original-input grammar only; reserved malformed commands cannot become model requests.</summary>
public sealed record NumberCommand(bool IsActivated, bool IsHelp = false,
    IntegerConversionRequest? Request = null, string? Error = null)
{
    public const int MaximumInputBytes = 256;
    public const int MaximumTokens = 7;
    public const int MaximumOutputBytes = 1024;
    public const string Syntax = "Use number help or number convert <integer> from <binary|octal|decimal|hex> to <binary|octal|decimal|hex>.";
    public const string Help = Syntax + " Signed mathematical Int64 only (-9223372036854775808 through 9223372036854775807). "
        + "ASCII digits and optional minus; no redundant leading zeros, -0, plus, prefixes, grouping, fractions or expressions. "
        + "Hex input is case-insensitive; output is lowercase with minus in every base, not two's complement. "
        + "Single ASCII spaces between tokens; at most 256 UTF-8 input bytes including the name prefix, 7 command-body tokens, 65 representation bytes and 1024 complete response bytes. Nothing is truncated. "
        + "Local deterministic conversion only; no model or external access.";

    public static NumberCommand? Parse(string input, string assistantName)
    {
        ArgumentNullException.ThrowIfNull(input);
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        var activated = false;
        var prefixValid = true;
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && !char.IsLetterOrDigit(text[name.Length]))
        {
            var rest = text[name.Length..];
            var start = 0;
            while (start < rest.Length && !char.IsLetterOrDigit(rest[start])) { start++; }
            var candidate = rest[start..];
            if (Reserved(candidate))
            {
                activated = true;
                prefixValid = string.Equals(rest, " " + candidate, StringComparison.Ordinal)
                    || string.Equals(rest, ", " + candidate, StringComparison.Ordinal);
                text = candidate;
            }
        }
        if (!Reserved(text)) { return null; }
        NumberCommand Invalid() => new(activated, Error: Syntax);
        if (!prefixValid || input.Length > MaximumInputBytes
            || Encoding.UTF8.GetByteCount(input) > MaximumInputBytes
            || !input.AsSpan().Trim(' ').SequenceEqual(input.AsSpan().Trim())
            || input.Any(char.IsControl)
            || text.Any(character => character is < ' ' or > '~'))
        {
            return Invalid();
        }
        var tokens = text.Split(' ');
        if (!tokens[0].Equals("number", StringComparison.OrdinalIgnoreCase)) { return Invalid(); }
        if (tokens.Length == 2 && tokens[1].Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            return new(activated, IsHelp: true);
        }
        if (tokens.Length != MaximumTokens || !tokens[1].Equals("convert", StringComparison.OrdinalIgnoreCase)
            || !tokens[3].Equals("from", StringComparison.OrdinalIgnoreCase)
            || !tokens[5].Equals("to", StringComparison.OrdinalIgnoreCase)
            || Base(tokens[4]) is not { } from || Base(tokens[6]) is not { } to)
        {
            return Invalid();
        }
        return new(activated, Request: new(tokens[2], from, to));
    }

    private static bool Reserved(string text) => text.StartsWith("number", StringComparison.OrdinalIgnoreCase)
        && (text.Length == 6 || !char.IsLetterOrDigit(text[6]));

    private static IntegerBase? Base(string text) => text.ToLowerInvariant() switch
    {
        "binary" => IntegerBase.Binary,
        "octal" => IntegerBase.Octal,
        "decimal" => IntegerBase.BaseTen,
        "hex" => IntegerBase.Hex,
        _ => null,
    };
}
