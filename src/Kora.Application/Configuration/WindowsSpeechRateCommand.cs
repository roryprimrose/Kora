using System.Text;
using Kora.Core.Commands;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record WindowsSpeechRateCommand(AppearanceCommandOperation Operation, string? Value = null, string? Error = null)
{
    public AudioPreferenceAdjustment? Adjustment { get; init; }

    public const string OptionId = "speech.windows-rate";
    public const string Syntax = "list rate settings | get speech.windows-rate | status speech.windows-rate | "
        + "set speech.windows-rate to <integer -10..10> | reset speech.windows-rate | speak faster | speak slower. "
        + "Relative steps are 1 native unit, stopping explicitly at -10/10; already at the boundary is unchanged. "
        + "Windows provider-native units only, normal/default/reset 0; no percent or words-per-minute mapping. Kokoro is unsupported and unchanged. Future speech only; no replay.";
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["list rate settings", "get " + OptionId, "status " + OptionId, "reset " + OptionId, "speak faster", "speak slower"];

    public static WindowsSpeechRateCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        var exactRelative = text.Equals("speak faster", StringComparison.OrdinalIgnoreCase)
            || text.Equals("speak slower", StringComparison.OrdinalIgnoreCase);
        if (!exactRelative && text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        var relative = text.StartsWith("speak faster", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("speak slower", StringComparison.OrdinalIgnoreCase);
        if (!relative && !text.StartsWith("list rate", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("get speech.windows-rate", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("status speech.windows-rate", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("set speech.windows-rate", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("reset speech.windows-rate", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        {
            return new(AppearanceCommandOperation.Clarify, Error: "Rate input exceeds 1024 UTF-8 bytes or contains controls. " + Syntax);
        }
        if (text.Equals("speak faster", StringComparison.OrdinalIgnoreCase))
        {
            return new(AppearanceCommandOperation.Set) { Adjustment = AudioPreferenceAdjustment.Increase };
        }
        if (text.Equals("speak slower", StringComparison.OrdinalIgnoreCase))
        {
            return new(AppearanceCommandOperation.Set) { Adjustment = AudioPreferenceAdjustment.Decrease };
        }
        if (text.Equals("list rate settings", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.List); }
        if (text.Equals("get " + OptionId, StringComparison.OrdinalIgnoreCase)
            || text.Equals("status " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Get); }
        if (text.Equals("reset " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Reset); }
        var prefix = "set " + OptionId + " to ";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && text.Length > prefix.Length)
        {
            return new(AppearanceCommandOperation.Set, text[prefix.Length..]);
        }
        return new(AppearanceCommandOperation.Clarify, Error: Syntax);
    }
}
