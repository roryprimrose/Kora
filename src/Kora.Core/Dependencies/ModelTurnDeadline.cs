namespace Kora.Core.Dependencies;

/// <summary>
/// A fixed active-time cancellation deadline that pauses with its turn's host waits and never fires for a
/// check-in turn. Without a lifetime it behaves exactly like <c>new CancellationTokenSource(limit, time)</c>.
/// </summary>
public sealed class ModelTurnDeadline : IDisposable
{
    private readonly Lock gate = new();
    private readonly ModelTurnLifetime? lifetime;
    private readonly CancellationTokenSource source;
    private readonly DateTimeOffset expiresAt;
    private readonly TimeProvider time;
    private bool disposed;

    public ModelTurnDeadline(ModelTurnLifetime? lifetime, TimeSpan limit, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(time);
        if (limit < TimeSpan.Zero) { limit = TimeSpan.Zero; }
        this.lifetime = lifetime;
        this.time = time;
        var now = time.GetUtcNow();
        expiresAt = DateTimeOffset.MaxValue - now <= limit ? DateTimeOffset.MaxValue : now + limit;
        if (lifetime?.UsesCheckIns == true)
        {
            source = new();
            return;
        }
        source = new(limit, time);
        if (lifetime is null) { return; }
        lifetime.HostWaitChanged += OnHostWaitChanged;
        if (lifetime.IsWaitingForHost) { OnHostWaitChanged(true); }
    }

    public CancellationToken Token => source.Token;

    public bool IsCancellationRequested => source.IsCancellationRequested;

    public Task CancelAsync() => source.CancelAsync();

    private void OnHostWaitChanged(bool waiting)
    {
        lock (gate)
        {
            if (disposed || source.IsCancellationRequested) { return; }
            var remaining = lifetime!.Remaining(expiresAt, time.GetUtcNow());
            source.CancelAfter(waiting || remaining == Timeout.InfiniteTimeSpan ? Timeout.InfiniteTimeSpan : remaining);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) { return; }
            disposed = true;
        }
        if (lifetime is not null) { lifetime.HostWaitChanged -= OnHostWaitChanged; }
        source.Dispose();
    }
}
