namespace Kora.Windows.Dependencies;

internal sealed class DevelopmentPreviewLease(
    DevelopmentPreviewCapability capabilities, DateTimeOffset expiresAt, TimeProvider time, Func<bool> eligible) : IDisposable
{
    private readonly CancellationTokenSource revocation = new();
    private readonly DateTimeOffset issuedAt = time.GetUtcNow();
    private volatile bool disposed;
    internal DevelopmentPreviewCapability Capabilities { get; } = capabilities;
    internal DateTimeOffset ExpiresAt { get; } = expiresAt;
    internal CancellationToken Revoked => revocation.Token;
    internal bool IsCurrent => !disposed && !revocation.IsCancellationRequested
        && issuedAt <= time.GetUtcNow() && time.GetUtcNow() < ExpiresAt && eligible();
    public void Dispose()
    {
        if (disposed) { return; }
        disposed = true;
        try { revocation.Cancel(); }
        finally { revocation.Dispose(); }
    }
}
