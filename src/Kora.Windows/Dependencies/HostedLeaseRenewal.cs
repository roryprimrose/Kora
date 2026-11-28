namespace Kora.Windows.Dependencies;

/// <summary>
/// Keeps one running host turn's hosted account lease current. Each renewal is the broker's audited in-place
/// revalidation; a refused or failed renewal stops the loop and the lease then lapses as before.
/// </summary>
internal sealed class HostedLeaseRenewal : IAsyncDisposable
{
    private readonly CancellationTokenSource cancellation;
    private readonly Task running;

    private HostedLeaseRenewal(Func<DateTimeOffset> expiresAt, Func<CancellationToken, ValueTask<bool>> renew,
        TimeProvider time, Action<Exception> failed, CancellationToken token)
    {
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        running = RunAsync(expiresAt, renew, time, failed, cancellation.Token);
    }

    /// <summary>Renewal starts once this much lease time remains.</summary>
    internal static TimeSpan Window { get; } = TimeSpan.FromMinutes(1);

    /// <summary>Starts renewal only for work owned by a host turn; other callers keep their fixed lease.</summary>
    internal static HostedLeaseRenewal? Start(Kora.Core.Dependencies.ModelTurnLifetime? turn, Func<DateTimeOffset> expiresAt,
        Func<CancellationToken, ValueTask<bool>> renew, TimeProvider time, Action<Exception> failed, CancellationToken token) =>
        turn is null ? null : new(expiresAt, renew, time, failed, token);

    private static async Task RunAsync(Func<DateTimeOffset> expiresAt, Func<CancellationToken, ValueTask<bool>> renew,
        TimeProvider time, Action<Exception> failed, CancellationToken token)
    {
        try
        {
            while (true)
            {
                var due = expiresAt() - Window - time.GetUtcNow();
                if (due > TimeSpan.Zero) { await Task.Delay(due, time, token).ConfigureAwait(false); }
                if (!await renew(token).ConfigureAwait(false)) { return; }
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
#pragma warning disable CA1031 // A failed renewal is reported, never treated as success: the lease lapses and later checks fail closed.
        catch (Exception exception) { failed(exception); }
#pragma warning restore CA1031
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "VSTHRD003",
        Justification = "Joins this owner's own renewal loop off any UI context before disposing its token source.")]
    public async ValueTask DisposeAsync()
    {
        try { await cancellation.CancelAsync().ConfigureAwait(false); }
        finally
        {
            await running.ConfigureAwait(false);
            cancellation.Dispose();
        }
    }
}
