using Kora.Core.Mcp;

namespace Kora.Windows.Mcp;

/// <summary>
/// Trusted host installation boundary, not server configuration or discovery.
/// Only list explicitly delegated accounts qualified for the exact stateless JSON HTTPS
/// profile, destination and egress policy. Unknown readiness must throw before credential access.
/// </summary>
public interface IQualifiedMcpAccountSource
{
    Task<IReadOnlyList<McpAccountChoice>> ReadAccountsAsync(CancellationToken token);
    Task<McpAccountLease> AcquireAsync(McpAccountReference account, CancellationToken token);
}
