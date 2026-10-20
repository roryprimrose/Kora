namespace Kora.Core.Numbers;

/// <summary>Signed mathematical Int64, never a fixed-width two's-complement encoding.</summary>
public static class IntegerBasePolicy
{
    public const int MaximumIntegerCharacters = 65;
    public const int MaximumRepresentationBytes = 65;

    public static IntegerConversionResult Convert(IntegerConversionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!Enum.IsDefined(request.From) || !Enum.IsDefined(request.To))
        {
            return new(IntegerConversionOutcome.InvalidBase);
        }
        var text = request.Literal;
        if (string.IsNullOrEmpty(text) || text.Length > MaximumIntegerCharacters)
        {
            return new(IntegerConversionOutcome.InvalidInteger);
        }
        var negative = text[0] == '-';
        var start = negative ? 1 : 0;
        if (start == text.Length || text[start] == '0' && (negative || text.Length - start > 1))
        {
            return new(IntegerConversionOutcome.InvalidInteger);
        }
        // Validate the complete literal before range evaluation; invalid digits never become overflow.
        foreach (var character in text.AsSpan(start))
        {
            if (Digit(character) is var digit && (digit < 0 || digit >= (int)request.From))
            {
                return new(IntegerConversionOutcome.InvalidInteger);
            }
        }
        var limit = negative ? 1UL << 63 : long.MaxValue;
        var radix = (ulong)request.From;
        ulong magnitude = 0;
        foreach (var character in text.AsSpan(start))
        {
            var digit = (ulong)Digit(character);
            if (magnitude > (limit - digit) / radix)
            {
                return new(IntegerConversionOutcome.Overflow);
            }
            magnitude = magnitude * radix + digit;
        }
        Span<char> output = stackalloc char[MaximumIntegerCharacters];
        var position = output.Length;
        var outputBase = (ulong)request.To;
        do
        {
            output[--position] = "0123456789abcdef"[(int)(magnitude % outputBase)];
            magnitude /= outputBase;
        } while (magnitude != 0);
        if (negative) { output[--position] = '-'; }
        return new(IntegerConversionOutcome.Succeeded, new string(output[position..]));
    }

    private static int Digit(char character) => character switch
    {
        >= '0' and <= '9' => character - '0',
        >= 'a' and <= 'f' => character - 'a' + 10,
        >= 'A' and <= 'F' => character - 'A' + 10,
        _ => -1,
    };
}
