using McpProcessAuthorization = Kora.Core.Mcp.McpConnectionIdentity.McpProcessAuthorization;

namespace Kora.Core.Mcp;

/// <summary>Trusted platform boundary. Resolve only the explicitly selected delegated account or separately consented local profile.
/// Disable redirects, proxies, cookies, ambient credentials and credential forwarding; pin admitted public DNS endpoints.
/// Unknown qualification/account/destination state must fail before sending anything.</summary>
public interface IMcpConnectionBroker
{
    Task<IMcpProcessPreparation> PrepareStdioAsync(McpConnectionIdentity identity, CancellationToken token) =>
        throw new InvalidOperationException("Native MCP process preparation is unavailable.");
    Task<IReadOnlyList<McpAccountChoice>> ReadAccountsAsync(CancellationToken token);
    Task<IMcpConnection> ConnectAsync(McpAccountReference account, CancellationToken token);
    Task<IMcpConnection> ConnectAsync(McpConnectionIdentity identity, McpProcessAuthorization? process, CancellationToken token) =>
        identity.Account is { } account ? ConnectAsync(account, token)
            : throw new InvalidOperationException("The local MCP process transport is unavailable.");
    /// <summary>Held native executable/directory identity, with no process started until separate consent.</summary>
    public interface IMcpProcessPreparation : IAsyncDisposable
    {
        McpConnectionIdentity Identity { get; }
        string NativeIdentity { get; }
        Task<IMcpConnection> ConnectAsync(McpProcessAuthorization authorization, CancellationToken token);
    }
}