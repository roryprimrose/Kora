using System.Text;
using Kora.Application.Configuration;
using Kora.Core.Commands;
using Kora.Core.Configuration;

namespace Kora.Application.Communication;

public sealed record ManualCallCommand(AppearanceCommandOperation Operation, bool? Active = null, string? Error = null)
{
    public const string OptionId = "call.manual-active";
    private const string NaturalOn = "turn on manual call protection";
    private const string NaturalOff = "turn off manual call protection";
    private const string NaturalStatus = "show manual call status";
    public const string Syntax = "list call settings | get call.manual-active | status call.manual-active | "
        + "set call.manual-active to on | set call.manual-active to off | reset call.manual-active | "
        + NaturalOn + " | " + NaturalOff + " | " + NaturalStatus + ". "
        + "Current run only; off/reset clear only the manual layer and never assert automatic no-call evidence. "
        + "Original input and current host/voice admission apply; protected calls do not permit voice-originated manual changes.";
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["list call settings", "get " + OptionId, "status " + OptionId,
            "set " + OptionId + " to on", "set " + OptionId + " to off", "reset " + OptionId,
            NaturalOn, NaturalOff, NaturalStatus];

    public static ManualCallCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        if (!text.StartsWith("list call", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("get call.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("status call.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("set call.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("reset call.", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("turn on manual call", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("turn off manual call", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("show manual call", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        {
            return new(AppearanceCommandOperation.Clarify, Error: "Call input exceeds 1024 UTF-8 bytes or contains controls. " + Syntax);
        }
        if (text.Equals("list call settings", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.List); }
        if (text.Equals("get " + OptionId, StringComparison.OrdinalIgnoreCase)
            || text.Equals("status " + OptionId, StringComparison.OrdinalIgnoreCase)
            || text.Equals(NaturalStatus, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Get); }
        if (text.Equals("set " + OptionId + " to on", StringComparison.OrdinalIgnoreCase)
            || text.Equals(NaturalOn, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Set, true); }
        if (text.Equals("set " + OptionId + " to off", StringComparison.OrdinalIgnoreCase)
            || text.Equals(NaturalOff, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Set, false); }
        if (text.Equals("reset " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Reset, false); }
        return new(AppearanceCommandOperation.Clarify, Error: Syntax);
    }
}
