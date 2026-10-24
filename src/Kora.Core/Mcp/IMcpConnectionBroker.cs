namespace Kora.Core.Mcp;

/// <summary>Trusted platform boundary. Resolve only the explicitly selected delegated account.
/// Disable redirects, proxies, cookies, ambient credentials and credential forwarding; pin admitted public DNS endpoints.
/// Unknown qualification/account/destination state must fail before sending anything.</summary>
public interface IMcpConnectionBroker
{
    Task<IReadOnlyList<McpAccountChoice>> ReadAccountsAsync(CancellationToken token);
    Task<IMcpConnection> ConnectAsync(McpAccountReference account, CancellationToken token);
}
