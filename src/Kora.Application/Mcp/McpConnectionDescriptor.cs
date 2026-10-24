using Kora.Core.Mcp;

namespace Kora.Application.Mcp;

/// <summary>Host-private source snapshot. Metadata inclusion is not account, connection or invocation authority.</summary>
internal sealed class McpConnectionDescriptor(
    string serverId, string serverName, string sourcePath, string sourceRevision,
    McpEndpoint endpoint, McpAccountReference account)
{
    internal string ServerId { get; } = serverId;
    internal string ServerName { get; } = serverName;
    internal string SourcePath { get; } = sourcePath;
    internal string SourceRevision { get; } = sourceRevision;
    internal McpEndpoint Endpoint { get; } = endpoint;
    internal McpAccountReference Account { get; } = account;

    public override string ToString() => nameof(McpConnectionDescriptor);
}
