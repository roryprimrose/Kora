using System.Text;
using Kora.Core.Commands;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

/// <summary>Original-user review/confirm commands; parsing never mutates a preference or dispatches work.</summary>
public sealed record ConversationExecutionCommand(ConversationExecutionOperation Operation,
    ConversationExecutionLimit? Limit = null)
{
    public const string OptionId = "conversations.execution-maximum";
    public const string Syntax = "get " + OptionId + " | review " + OptionId + " to 1|2 | confirm " + OptionId + " | cancel " + OptionId;
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["get " + OptionId, "review " + OptionId + " to 1", "review " + OptionId + " to 2",
         "confirm " + OptionId, "cancel " + OptionId];

    public static ConversationExecutionCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        { text = text[name.Length..].TrimStart(' ', ',', '\t'); }
        if (!text.Contains(OptionId, StringComparison.OrdinalIgnoreCase)) { return null; }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        { return new(ConversationExecutionOperation.Clarify); }
        if (text.Equals("get " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(ConversationExecutionOperation.Get); }
        if (text.Equals("confirm " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(ConversationExecutionOperation.Confirm); }
        if (text.Equals("cancel " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(ConversationExecutionOperation.Cancel); }
        foreach (var value in new[] { "1", "2" })
        {
            if (text.Equals($"review {OptionId} to {value}", StringComparison.OrdinalIgnoreCase))
            { return new(ConversationExecutionOperation.Review, ConversationExecutionLimit.Parse(value)); }
        }
        return new(ConversationExecutionOperation.Clarify);
    }
}
