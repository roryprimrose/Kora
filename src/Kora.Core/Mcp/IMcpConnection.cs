using System.Net;

namespace Kora.Core.Mcp;

/// <summary>A single owned authenticated transport. Identity reports are observations, not grants.</summary>
public interface IMcpConnection : IAsyncDisposable
{
    McpAccountReference Account { get; }
    McpEndpoint Endpoint { get; }
    IReadOnlyList<IPAddress> RemoteAddresses { get; }
    CancellationToken AdmissionWithdrawn => new(canceled: true);
    Task<bool> IsCurrentAsync(CancellationToken token);
    Task<McpConnectionReply<McpServerCapabilities>> InitializeAsync(CancellationToken token);
    Task<McpConnectionReply<McpCapabilityPage>> ListAsync(McpCapabilityKind kind, string? cursor, CancellationToken token);
    Task<McpConnectionReply<McpRemoteResult>> InvokeAsync(string name, McpToolSchema schema, string arguments, CancellationToken token);
    Task<McpConnectionReply<McpTextDocument>> ReadResourceAsync(string uri, CancellationToken token);
    Task<McpConnectionReply<McpTextDocument>> ReadPromptAsync(McpCapability prompt, string arguments, CancellationToken token);
}
