using System.Text;

using Kora.Core.Commands;
using Kora.Core.Communication;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record CommunicationActivityCommand(
    AppearanceCommandOperation Operation, CommunicationActivitySelection Selection = CommunicationActivitySelection.None,
    string? Error = null)
{
    public const string OptionId = "calls.activity-source";
    public const string Syntax = "list call observation settings | get calls.activity-source | status calls.activity-source | "
        + "set calls.activity-source to None|WindowsCommunicationsAudio | reset calls.activity-source";
    public static IReadOnlyList<string> FixedPhrases { get; } =
    [
        "list call observation settings", "get " + OptionId, "status " + OptionId, "reset " + OptionId,
        "set " + OptionId + " to None", "set " + OptionId + " to WindowsCommunicationsAudio",
    ];

    public static CommunicationActivityCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        { text = text[name.Length..].TrimStart(' ', ',', '\t'); }
        if (!text.StartsWith("list call observation", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("get calls.activity", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("status calls.activity", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("set calls.activity", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("reset calls.activity", StringComparison.OrdinalIgnoreCase)) { return null; }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        { return new(AppearanceCommandOperation.Clarify, Error: Syntax); }
        if (text.Equals(FixedPhrases[0], StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.List); }
        if (text.Equals(FixedPhrases[1], StringComparison.OrdinalIgnoreCase)
            || text.Equals(FixedPhrases[2], StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Get); }
        if (text.Equals(FixedPhrases[3], StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Reset); }
        if (text.Equals(FixedPhrases[4], StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Set); }
        if (text.Equals(FixedPhrases[5], StringComparison.OrdinalIgnoreCase))
        { return new(AppearanceCommandOperation.Set, CommunicationActivitySelection.WindowsCommunicationsAudio); }
        return new(AppearanceCommandOperation.Clarify, Error: Syntax);
    }
}