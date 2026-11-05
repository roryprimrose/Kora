using Kora.Core.Mcp;
using Kora.Core.Hosting;

namespace Kora.Application.Mcp;

/// <summary>Returns the exact selected tool name, or null when selection is cancelled or dismissed.</summary>
public delegate Task<string?> McpToolSelector(HostRequest request, HostRevision generation, DateTimeOffset expiresAt,
    IReadOnlyList<McpDiscoveredTool> tools,
    Func<bool> admitted, CancellationToken token);
