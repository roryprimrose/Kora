using Kora.Core.Configuration;

namespace Kora.Core.Calculations;

public sealed class CalculateCommand
{
    private CalculateCommand(string? expression, bool help, bool activated)
    {
        Expression = expression;
        IsHelp = help;
        IsActivated = activated;
    }

    public const string Syntax = "calculate <expression> | calculate help. "
        + "Use decimal digits, a decimal point, +, -, *, /, parentheses and unary signs; ASCII spaces only. "
        + "Limits: 512 expression characters, 128 tokens, 16 nested parentheses/unary signs, 31 result characters. "
        + "Literals must be exactly representable as decimal; checked decimal operations use .NET decimal rounding. "
        + "No exponents, functions, units, scripts, shell, model or network.";
    public static IEnumerable<string> DiscoveryPhrases => ["calculate", "calculate help"];
    public string? Expression { get; }
    public bool IsHelp { get; }
    public bool IsActivated { get; }
    public override string ToString() => nameof(CalculateCommand);

    public static CalculateCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        var activated = text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (text[name.Length] == ',' || char.IsWhiteSpace(text[name.Length]));
        if (activated)
        {
            var body = text[name.Length..].TrimStart(' ', ',', '\t');
            // A valid assistant name may itself be "Calculate"; it cannot swallow the typed command.
            if (body.StartsWith("calculate", StringComparison.OrdinalIgnoreCase)) { text = body; }
            else { activated = false; }
        }
        if (!text.StartsWith("calculate", StringComparison.OrdinalIgnoreCase)) { return null; }
        if (input.Length > MaximumInputCharacters || input.Any(char.IsControl))
        {
            return new(null, false, activated);
        }
        if (text.Equals("calculate help", StringComparison.OrdinalIgnoreCase)) { return new(null, true, activated); }
        return new(text.StartsWith("calculate ", StringComparison.OrdinalIgnoreCase) ? text[10..] : null, false, activated);
    }

    public const int MaximumInputCharacters = DecimalCalculator.MaximumExpressionCharacters
        + AssistantNameRules.MaximumLength + 12;
}
