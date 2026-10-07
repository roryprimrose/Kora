using System.Text;
using Kora.Core.Commands;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record OutputDeviceCommand(AppearanceCommandOperation Operation, string? Value = null, string? Error = null)
{
    public const string OptionId = "speech.output-device";
    public const string Syntax = "list output settings | get speech.output-device | status speech.output-device | "
        + "set speech.output-device to <exact listed endpoint ID> | reset speech.output-device. "
        + "System is system-default; reset removes only the Kora output override. No playback or Windows setting change.";
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["list output settings", "get " + OptionId, "status " + OptionId, "reset " + OptionId];

    public static OutputDeviceCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        if (!text.StartsWith("list output", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("get speech.output", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("status speech.output", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("set speech.output", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("reset speech.output", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        {
            return new(AppearanceCommandOperation.Clarify, Error: "Output input exceeds 1024 UTF-8 bytes or contains controls. " + Syntax);
        }
        if (text.Equals("list output settings", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.List); }
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
