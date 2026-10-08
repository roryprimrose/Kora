using System.Text;
using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record InCallFeedbackCommand(AppearanceCommandOperation Operation, InCallFeedbackMode? Value = null, string? Error = null)
{
    public const string OptionId = "calls.feedback-mode";
    public const string Syntax = "list call feedback settings | get calls.feedback-mode | status calls.feedback-mode | "
        + "set calls.feedback-mode to Voice|UI|Both|Inherit | reset calls.feedback-mode";
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["list call feedback settings", "get " + OptionId, "status " + OptionId, "reset " + OptionId,
            "set " + OptionId + " to Voice", "set " + OptionId + " to UI",
            "set " + OptionId + " to Both", "set " + OptionId + " to Inherit"];

    public static InCallFeedbackCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        if (!text.StartsWith("list call feedback", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("get calls.feedback", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("status calls.feedback", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("set calls.feedback", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("reset calls.feedback", StringComparison.OrdinalIgnoreCase)) { return null; }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        {
            return new(AppearanceCommandOperation.Clarify, Error: "Call feedback input exceeds 1024 UTF-8 bytes or contains controls. " + Syntax);
        }
        if (text.Equals("list call feedback settings", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.List); }
        if (text.Equals("get " + OptionId, StringComparison.OrdinalIgnoreCase)
            || text.Equals("status " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Get); }
        if (text.Equals("reset " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Reset); }
        var prefix = "set " + OptionId + " to ";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            foreach (var mode in Enum.GetValues<InCallFeedbackMode>())
            {
                if (text[prefix.Length..].Equals(mode.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    return new(AppearanceCommandOperation.Set, mode);
                }
            }
        }
        return new(AppearanceCommandOperation.Clarify, Error: Syntax);
    }
}
