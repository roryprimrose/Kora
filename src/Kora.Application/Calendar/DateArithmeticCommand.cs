using Kora.Core.Calendar;
using Kora.Core.Configuration;

namespace Kora.Application.Calendar;

public sealed record DateArithmeticCommand(
    DateArithmeticOperation Operation, DateOnly Start = default, DateOnly End = default, int Days = 0, string? Error = null)
{
    public const int MaximumInputLength = 256;
    public static IReadOnlyList<string> FixedPhrases { get; } = ["date help", "date difference", "date add"];
    public const string Syntax = "Use date difference <yyyy-MM-dd> <yyyy-MM-dd>, date add <yyyy-MM-dd> <signed-days>, or date help. "
        + "Exact single ASCII spaces; dates 0001-01-01 through 9999-12-31; canonical days -3652058 through 3652058 (0, 1, -1; no + sign or leading zeroes).";

    public static DateArithmeticCommand? Parse(string input, string assistantName)
    {
        ArgumentNullException.ThrowIfNull(input);
        var name = AssistantNameRules.Normalize(assistantName);
        var text = input;
        // Strip the activation name first, even when the name itself starts with the reserved family.
        if (text.StartsWith(name + ", ", StringComparison.OrdinalIgnoreCase)
            && (IsReserved(text[(name.Length + 2)..]) || !IsReserved(text)))
        {
            text = text[(name.Length + 2)..];
        }
        else if (text.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase)
            && (IsReserved(text[(name.Length + 1)..]) || !IsReserved(text)))
        {
            text = text[(name.Length + 1)..];
        }
        if (!IsReserved(text))
        {
            // Malformed activation delimiters still reserve attempted date commands, without repairing them.
            var candidate = input.TrimStart();
            if (candidate.StartsWith(name, StringComparison.OrdinalIgnoreCase) && candidate.Length > name.Length
                && !char.IsLetterOrDigit(candidate[name.Length]))
            {
                candidate = candidate[name.Length..].TrimStart(' ', ',', '\t', '\r', '\n', ':', ';');
            }
            return IsReserved(candidate) ? new(DateArithmeticOperation.Invalid, Error: Syntax) : null;
        }
        if (input.Length > MaximumInputLength || input.Any(char.IsControl) || !text.Equals(text.Trim(), StringComparison.Ordinal))
        {
            return new(DateArithmeticOperation.Invalid, Error: Syntax);
        }
        var parts = text.Split(' ');
        try
        {
            if (text.Equals("date help", StringComparison.OrdinalIgnoreCase)) { return new(DateArithmeticOperation.Help); }
            if (parts.Length == 4 && parts[0].Equals("date", StringComparison.OrdinalIgnoreCase))
            {
                if (parts[1].Equals("difference", StringComparison.OrdinalIgnoreCase))
                {
                    return new(DateArithmeticOperation.Difference,
                        CalendarDateArithmetic.ParseDate(parts[2]), CalendarDateArithmetic.ParseDate(parts[3]));
                }
                if (parts[1].Equals("add", StringComparison.OrdinalIgnoreCase))
                {
                    return new(DateArithmeticOperation.Add,
                        CalendarDateArithmetic.ParseDate(parts[2]), Days: CalendarDateArithmetic.ParseDays(parts[3]));
                }
            }
        }
        catch (InvalidDataException exception)
        {
            return new(DateArithmeticOperation.Invalid, Error: exception.Message);
        }
        return new(DateArithmeticOperation.Invalid, Error: Syntax);
    }

    private static bool IsReserved(string text)
    {
        var candidate = text.TrimStart();
        return candidate.StartsWith("date", StringComparison.OrdinalIgnoreCase)
            && (candidate.Length == 4 || !char.IsLetterOrDigit(candidate[4]));
    }
}
