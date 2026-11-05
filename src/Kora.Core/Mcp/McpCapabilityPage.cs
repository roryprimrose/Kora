using System.Collections.Immutable;

namespace Kora.Core.Mcp;

public sealed record McpCapabilityPage(ImmutableArray<McpCapability> Items, string? NextCursor);