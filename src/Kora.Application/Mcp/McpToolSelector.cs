using Kora.Core.Mcp;
using Kora.Core.Hosting;

namespace Kora.Application.Mcp;

public delegate Task<string?> McpToolSelector(HostRequest request, HostRevision generation, DateTimeOffset expiresAt,
    IReadOnlyList<McpDiscoveredTool> tools,
    Func<bool> admitted, CancellationToken token);
