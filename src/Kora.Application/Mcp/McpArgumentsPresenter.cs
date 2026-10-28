using Kora.Core.Hosting;
using Kora.Core.Mcp;

namespace Kora.Application.Mcp;

public delegate Task<string?> McpArgumentsPresenter(HostRequest request, HostRevision generation,
    DateTimeOffset expiresAt, McpModelTool tool, Func<bool> admitted, CancellationToken token);
