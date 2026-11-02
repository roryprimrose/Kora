using System.Text;

using Kora.Core.Commands;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record OrdinaryTurnDeadlineCommand(AppearanceCommandOperation Operation, string? Value = null, string? Error = null)
{
    public const string OptionId = "work.active-deadline-minutes";
    public const string Syntax = "get " + OptionId + " | status " + OptionId + " | set " + OptionId
        + " to <integer 1-60> | reset " + OptionId + ". Default/reset 5; future dispatched ordinary turns only, within earlier context and fixed provider/tool limits.";
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["get " + OptionId, "status " + OptionId, "reset " + OptionId];

    public static OrdinaryTurnDeadlineCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        { text = text[name.Length..].TrimStart(' ', ',', '\t'); }
        if (!text.Contains(OptionId, StringComparison.OrdinalIgnoreCase)) { return null; }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        { return new(AppearanceCommandOperation.Clarify, Error: Syntax); }
        if (text.Equals("get " + OptionId, StringComparison.OrdinalIgnoreCase)
            || text.Equals("status " + OptionId, StringComparison.OrdinalIgnoreCase))
        { return new(AppearanceCommandOperation.Get); }
        if (text.Equals("reset " + OptionId, StringComparison.OrdinalIgnoreCase))
        { return new(AppearanceCommandOperation.Reset); }
        var prefix = "set " + OptionId + " to ";
        return text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? new(AppearanceCommandOperation.Set, text[prefix.Length..])
            : new(AppearanceCommandOperation.Clarify, Error: Syntax);
    }
}
