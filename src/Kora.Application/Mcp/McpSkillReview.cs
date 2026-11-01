namespace Kora.Application.Mcp;

/// <summary>Native-only skill lineage included in the exact MCP approval digest.</summary>
public sealed record McpSkillReview(string Identity, string Revision, string OriginalRequest, string ArgumentsDigest);
