using Kora.Core.Mcp;
using Kora.Core.Customizations;

namespace Kora.Application.Mcp;

/// <summary>Host-private source snapshot. Metadata inclusion is not account, connection or invocation authority.</summary>
internal sealed class McpConnectionDescriptor(
    string serverId, string serverName, string sourcePath, string sourceRevision,
    McpConnectionIdentity identity, long useControlRevision, CustomizationScope? scope = null,
    long sourceControlRevision = 0)
{
    internal string ServerId { get; } = serverId;
    internal string ServerName { get; } = serverName;
    internal string SourcePath { get; } = sourcePath;
    internal string SourceRevision { get; } = sourceRevision;
    internal McpConnectionIdentity Identity { get; } = identity;
    internal McpEndpoint Endpoint => Identity.Account?.Destination
        ?? throw new InvalidOperationException("A local process has no HTTPS endpoint.");
    internal McpAccountReference Account => Identity.Account
        ?? throw new InvalidOperationException("A local process has no delegated account.");
    internal long UseControlRevision { get; } = useControlRevision;
    internal CustomizationScope? Scope { get; } = scope;
    internal long SourceControlRevision { get; } = sourceControlRevision;

    internal McpConnectionDescriptor(string serverId, string serverName, string sourcePath, string sourceRevision,
        McpEndpoint endpoint, McpAccountReference account, long useControlRevision, CustomizationScope? scope = null,
        long sourceControlRevision = 0)
        : this(serverId, serverName, sourcePath, sourceRevision, new McpConnectionIdentity(account),
            useControlRevision, scope, sourceControlRevision)
    {
        if (endpoint != account.Destination) { throw new InvalidDataException("MCP account destination mismatch."); }
    }

    public override string ToString() => nameof(McpConnectionDescriptor);
}
