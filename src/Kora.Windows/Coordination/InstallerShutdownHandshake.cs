namespace Kora.Windows.Coordination;

internal static class InstallerShutdownHandshake
{
    internal static async Task RequestStopAsync(Stream pipe, Func<CancellationToken, Task> waitForExit,
        CancellationToken cancellationToken)
    {
        await InstanceProtocol.WriteAsync(pipe, new InstanceMessage(InstanceProtocol.Version, "InstallerStop"),
            cancellationToken).ConfigureAwait(false);
        var reply = await InstanceProtocol.ReadAsync(pipe, cancellationToken).ConfigureAwait(false);
        if (!string.Equals(reply.Kind, "StopRequested", StringComparison.Ordinal) ||
            reply.Epoch != Guid.Empty || reply.Transaction != Guid.Empty || reply.Ticket.Length != 0 ||
            reply.Detail.Length != 0)
        {
            throw new InvalidOperationException(
                "Kora did not approve installer shutdown. Close Kora normally and retry; incompatible older releases cannot be stopped by setup.");
        }

        using var waiting = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var exited = waitForExit(waiting.Token);
        try
        {
            try
            {
                _ = await InstanceProtocol.ReadAsync(pipe, waiting.Token).ConfigureAwait(false);
                throw new InvalidOperationException(
                    "Kora declined or could not complete the reviewed shutdown. No package changes were admitted.");
            }
            catch (IOException)
            {
                // Pipe closure may precede disposal. Only the authenticated process's actual exit permits return.
                await exited.ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            await waiting.CancelAsync().ConfigureAwait(false);
            try { await exited.ConfigureAwait(false); }
            catch (OperationCanceledException) when (waiting.IsCancellationRequested)
            {
                // Retire only the process waiter; no process is terminated or treated as quiescent.
            }
        }
    }
}
