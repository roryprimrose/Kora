using Kora.Core.Configuration;
using Kora.Core.Conversation;

namespace Kora.Application.Mcp;

public sealed record McpCommand(string? Body, bool Stop, string? Error = null, bool Explicit = false)
{
    public static IReadOnlyList<string> FixedPhrases { get; } = ["use selected mcp tool", "invoke selected mcp tool", "stop mcp"];

    public static McpCommand? Parse(string input, string assistantName)
    {
        ArgumentNullException.ThrowIfNull(input);
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        { text = text[name.Length..].TrimStart(' ', ',', '\t'); }
        if (string.Equals(text, "stop mcp", StringComparison.OrdinalIgnoreCase)) { return new(null, true); }
        if (string.Equals(text, "invoke selected mcp tool", StringComparison.OrdinalIgnoreCase))
        { return new(null, false, Explicit: true); }
        const string phrase = "use selected mcp tool";
        if (!text.StartsWith(phrase, StringComparison.OrdinalIgnoreCase)) { return null; }
        if (text.Length <= phrase.Length || text[phrase.Length] != ' ')
        { return new(null, false, "Use use selected mcp tool <task> with the exact foreground session, server and delegated account selected."); }
        var body = text[(phrase.Length + 1)..];
        try { ConversationText.Validate(body, submission: true); }
        catch (Exception exception) when (exception is InvalidDataException or ConversationCredentialException)
        { return new(null, false, "The MCP task must be bounded, nonempty text without credential-like content. Nothing was submitted."); }
        return new(body, false);
    }
}
