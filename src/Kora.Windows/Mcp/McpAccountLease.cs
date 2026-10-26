using Kora.Core.Mcp;

namespace Kora.Windows.Mcp;

/// <summary>
/// An exact account/profile/egress lease backed by an OS-protected credential.
/// Withdrawal must cancel AdmissionWithdrawn; IsCurrentAsync also revalidates all three.
/// A retained account reference or token alone is never current authority.
/// </summary>
public abstract class McpAccountLease : IDisposable
{
    public abstract McpAccountReference Account { get; }
    public abstract string AccessToken { get; }
    public abstract CancellationToken AdmissionWithdrawn { get; }
    public abstract Task<bool> IsCurrentAsync(CancellationToken token);
    public abstract void Dispose();
}
