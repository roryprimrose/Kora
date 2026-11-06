using Kora.Core.Hosting;
using Kora.Core.Mcp;

namespace Kora.Application.Mcp;

/// <summary>Returns an exact presented capability, or null on cancellation.</summary>
public delegate Task<McpCapability?> McpCapabilitySelector(HostRequest request, HostRevision generation,
    DateTimeOffset expiresAt, IReadOnlyList<McpCapability> capabilities, Func<bool> admitted, CancellationToken token);