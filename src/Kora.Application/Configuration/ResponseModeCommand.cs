using System.Text;
using Kora.Core.Commands;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed record ResponseModeCommand(AppearanceCommandOperation Operation, ResponseOutputMode? Value = null, string? Error = null)
{
    public const string OptionId = "responses.default-mode";
    public const string Syntax = "list response settings | get responses.default-mode | status responses.default-mode | "
        + "set responses.default-mode to Hybrid|VoiceOnly|VisualOnly | reset responses.default-mode";
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["list response settings", "get " + OptionId, "status " + OptionId, "reset " + OptionId,
            "set " + OptionId + " to Hybrid", "set " + OptionId + " to VoiceOnly", "set " + OptionId + " to VisualOnly"];

    public static ResponseModeCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        if (!text.StartsWith("list response", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("get responses.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("status responses.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("set responses.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("reset responses.", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        {
            return new(AppearanceCommandOperation.Clarify, Error: "Response input exceeds 1024 UTF-8 bytes or contains controls. " + Syntax);
        }
        if (text.Equals("list response settings", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.List); }
        if (text.Equals("get " + OptionId, StringComparison.OrdinalIgnoreCase)
            || text.Equals("status " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Get); }
        if (text.Equals("reset " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Reset); }
        var prefix = "set " + OptionId + " to ";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            var value = text[prefix.Length..];
            foreach (var mode in Enum.GetValues<ResponseOutputMode>())
            {
                if (value.Equals(mode.ToString(), StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Set, mode); }
            }
        }
        return new(AppearanceCommandOperation.Clarify, Error: Syntax);
    }
}
