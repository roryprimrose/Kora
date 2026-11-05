using System.Collections.Immutable;
using System.Text.Json;

namespace Kora.Core.Mcp;

public sealed class McpTextDocument
{
    public McpTextDocument(IEnumerable<McpTextBlock> blocks)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        Blocks = blocks.Take(17).ToImmutableArray();
        if (Blocks.IsEmpty || Blocks.Length > 16 || Blocks.Any(block => block is null || block.Text is null
                || block.Role is not (null or "user" or "assistant"))
            || JsonSerializer.SerializeToUtf8Bytes(Blocks).Length > McpCapabilityProtocol.MaximumContentBytes)
        { throw new InvalidDataException("The complete MCP text document is empty, unsupported or exceeds its bound."); }
    }

    public ImmutableArray<McpTextBlock> Blocks { get; }
    public string Json => JsonSerializer.Serialize(Blocks);
    public string Display => string.Join("\n\n", Blocks.Select(block =>
        (block.Role is null ? string.Empty : "Template role: " + block.Role + "\n")
        + (block.Uri is null ? string.Empty : "Resource: " + block.Uri + "\n") + block.Text));
    public override string ToString() => nameof(McpTextDocument);
}