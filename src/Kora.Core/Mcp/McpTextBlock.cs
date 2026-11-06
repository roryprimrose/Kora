namespace Kora.Core.Mcp;

/// <summary>Complete external text. Declared prompt roles are template data, not conversation history.</summary>
public sealed record McpTextBlock(string Text, string? Uri, string? MimeType, string? Role)
{
    public override string ToString() => nameof(McpTextBlock);
}