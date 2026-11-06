using Kora.Core.Configuration;
using Kora.Core.Conversation;
using Kora.Core.Mcp;

namespace Kora.Application.Mcp;

public sealed record McpCommand(string? Body, bool Stop, string? Error = null, bool Explicit = false,
    McpCapabilityKind? ContentKind = null, bool Manage = false)
{
    public static IReadOnlyList<string> FixedPhrases { get; } =
    [
        "use selected mcp tool", "invoke selected mcp tool", "stop mcp", "manage selected mcp capabilities",
        "read selected mcp resource", "read selected mcp resource template", "preview selected mcp prompt",
        "use selected mcp resource", "use selected mcp resource template", "use selected mcp prompt",
    ];

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
        if (string.Equals(text, "manage selected mcp capabilities", StringComparison.OrdinalIgnoreCase))
        { return new(null, false, Manage: true); }
        foreach (var (read, use, kind) in new[]
        {
            ("read selected mcp resource template", "use selected mcp resource template", McpCapabilityKind.ResourceTemplate),
            ("read selected mcp resource", "use selected mcp resource", McpCapabilityKind.Resource),
            ("preview selected mcp prompt", "use selected mcp prompt", McpCapabilityKind.Prompt),
        })
        {
            if (string.Equals(text, read, StringComparison.OrdinalIgnoreCase)) { return new(null, false, Explicit: true, ContentKind: kind); }
            if (text.StartsWith(use, StringComparison.OrdinalIgnoreCase))
            { return ParseTask(text, use, kind); }
        }
        return ParseTask(text, "use selected mcp tool", null);
    }

    private static McpCommand? ParseTask(string text, string phrase, McpCapabilityKind? kind)
    {
        if (!text.StartsWith(phrase, StringComparison.OrdinalIgnoreCase)) { return null; }
        if (text.Length <= phrase.Length || text[phrase.Length] != ' ')
        { return new(null, false, "Use " + phrase + " <task> with the exact foreground session, server and delegated account selected."); }
        var body = text[(phrase.Length + 1)..];
        try { ConversationText.Validate(body, submission: true); }
        catch (Exception exception) when (exception is InvalidDataException or ConversationCredentialException)
        { return new(null, false, "The MCP task must be bounded, nonempty text without credential-like content. Nothing was submitted."); }
        return new(body, false, ContentKind: kind);
    }
}