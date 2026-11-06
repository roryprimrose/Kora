using Kora.Core.Hosting;
using Kora.Core.Mcp;

namespace Kora.Application.Mcp;

/// <summary>Collects original JSON string arguments; submission is not approval.</summary>
public delegate Task<string?> McpContentInputPresenter(HostRequest request, HostRevision generation,
    DateTimeOffset expiresAt, McpCapability capability, Func<bool> admitted, CancellationToken token);