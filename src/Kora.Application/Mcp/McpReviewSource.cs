namespace Kora.Application.Mcp;

public sealed record McpReviewSource(string ServerName, string Path, string Revision)
{
    public override string ToString() => nameof(McpReviewSource);
}
