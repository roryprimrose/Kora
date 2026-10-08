using System.Text;
using Kora.Core.Commands;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed record SpeechTextCommand(AppearanceCommandOperation Operation, SpeechTextMode? Value = null, string? Error = null)
{
    public const string OptionId = "display.speech-text";
    public const string Syntax = "list speech text settings | get display.speech-text | status display.speech-text | "
        + "set display.speech-text to Off|CurrentUtterance | reset display.speech-text";
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["list speech text settings", "get " + OptionId, "status " + OptionId, "reset " + OptionId,
            "set " + OptionId + " to Off", "set " + OptionId + " to CurrentUtterance"];

    public static SpeechTextCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        if (!text.StartsWith("list speech text", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("get display.speech-text", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("status display.speech-text", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("set display.speech-text", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("reset display.speech-text", StringComparison.OrdinalIgnoreCase)) { return null; }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        {
            return new(AppearanceCommandOperation.Clarify, Error: "Speech-text input exceeds its byte limit or contains controls. " + Syntax);
        }
        if (text.Equals("list speech text settings", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.List); }
        if (text.Equals("get " + OptionId, StringComparison.OrdinalIgnoreCase)
            || text.Equals("status " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Get); }
        if (text.Equals("reset " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Reset); }
        foreach (var mode in Enum.GetValues<SpeechTextMode>())
        {
            if (text.Equals("set " + OptionId + " to " + mode, StringComparison.OrdinalIgnoreCase))
            {
                return new(AppearanceCommandOperation.Set, mode);
            }
        }
        return new(AppearanceCommandOperation.Clarify, Error: Syntax);
    }
}
