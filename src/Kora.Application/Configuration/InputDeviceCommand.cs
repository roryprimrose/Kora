using System.Text;

using Kora.Core.Commands;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record InputDeviceCommand(
    AppearanceCommandOperation Operation, string? Value = null, string? Error = null)
{
    public const string OptionId = "speech.input-device";
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["list input settings", "get " + OptionId, "reset " + OptionId];
    public const string Syntax = "list input settings | get speech.input-device | "
        + "set speech.input-device to <exact listed endpoint ID> | reset speech.input-device. "
        + "System is system-default. Reset selects System only; no consent, permission or listening change.";

    public static InputDeviceCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        if (!text.StartsWith("list input", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("get speech.input", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("set speech.input", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("reset speech.input", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        {
            return new(AppearanceCommandOperation.Clarify, Error: "Input exceeds 1024 UTF-8 bytes or contains controls. " + Syntax);
        }
        if (text.Equals("list input settings", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.List); }
        if (text.Equals("get " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Get); }
        if (text.Equals("reset " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Reset); }
        var prefix = "set " + OptionId + " to ";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && text.Length > prefix.Length)
        {
            return new(AppearanceCommandOperation.Set, text[prefix.Length..]);
        }
        return new(AppearanceCommandOperation.Clarify, Error: Syntax);
    }
}
