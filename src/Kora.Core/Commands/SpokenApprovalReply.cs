using Kora.Core.Configuration;

namespace Kora.Core.Commands;

public sealed record SpokenApprovalReply(SpokenApprovalReply.Operation? Decision)
{
    public enum Operation
    {
        Once,
        Session,
        Deny,
    }

    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["approve once", "approve for session", "deny"];

    public static SpokenApprovalReply? Parse(string input, string assistantName)
    {
        var text = input.Trim().TrimEnd('.', '?', '!').TrimEnd();
        var name = AssistantNameRules.ResolveActivationName(text, assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        { text = text[name.Length..].TrimStart(' ', ','); }
        if (!text.StartsWith("approve", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "deny", StringComparison.OrdinalIgnoreCase))
        { return null; }
        if (input.Length > 1024 || input.Any(char.IsControl)) { return new(Decision: null); }
        return text.ToLowerInvariant() switch
        {
            "approve once" => new(Operation.Once),
            "approve for session" => new(Operation.Session),
            "deny" => new(Operation.Deny),
            _ => new(Decision: null),
        };
    }
}
