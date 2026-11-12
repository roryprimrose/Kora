namespace Kora.Windows.Coordination;

internal static class InstanceShutdownHandshake
{
    internal static async Task RequestStopAsync(Stream pipe, Func<CancellationToken, Task> waitForExit,
        string requestKind, CancellationToken cancellationToken)
    {
        if (requestKind is not ("InstallerStop" or "Exit"))
        { throw new ArgumentException("The shutdown request kind is unsupported.", nameof(requestKind)); }
        await InstanceProtocol.WriteAsync(pipe, new InstanceMessage(InstanceProtocol.Version, requestKind),
            cancellationToken).ConfigureAwait(false);
        var reply = await InstanceProtocol.ReadAsync(pipe, cancellationToken).ConfigureAwait(false);
        if (reply.Kind is not "StopRequested" || reply.Epoch != Guid.Empty || reply.Transaction != Guid.Empty
            || reply.Ticket.Length != 0 || reply.Detail.Length != 0)
        {
            throw new InvalidOperationException(requestKind is "InstallerStop"
                ? "Kora did not approve installer shutdown. Close Kora normally and retry; incompatible older releases cannot be stopped by setup."
                : "Kora did not accept graceful exit. The owner may be incompatible, busy or denied; no process was killed.");
        }
        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var exited = waitForExit(waiting.Token);
        try
        {
            try
            {
                _ = await InstanceProtocol.ReadAsync(pipe, waiting.Token).ConfigureAwait(false);
                throw new InvalidOperationException(requestKind is "InstallerStop"
                    ? "Kora declined or could not complete the reviewed shutdown. No package changes were admitted."
                    : "Kora cancelled or could not complete graceful exit. No process was killed.");
            }
            catch (IOException)
            {
                await exited.ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            await waiting.CancelAsync().ConfigureAwait(false);
            try { await exited.ConfigureAwait(false); }
            catch (OperationCanceledException) when (waiting.IsCancellationRequested) { }
        }
    }
}
