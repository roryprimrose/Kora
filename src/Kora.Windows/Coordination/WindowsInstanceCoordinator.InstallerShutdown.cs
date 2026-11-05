using System.IO.Pipes;

namespace Kora.Windows.Coordination;

public sealed partial class WindowsInstanceCoordinator
{
    private bool installerShutdownPending;
    private Func<CancellationToken, Task<bool>>? installerShutdown;

    public void BindInstallerShutdown(Func<CancellationToken, Task<bool>>? shutdown)
    {
        lock (sync) { installerShutdown = shutdown; }
    }

    private async Task HandleInstallerShutdownAsync(NamedPipeServerStream pipe, int processId,
        InstanceMessage request, CancellationToken cancellationToken)
    {
        using var peer = InstallerShutdownPeer.Open(processId, sid, sessionId);
        if (request.Epoch != Guid.Empty || request.Transaction != Guid.Empty ||
            request.Ticket.Length != 0 || request.Detail.Length != 0)
        {
            throw new InvalidDataException("Installer shutdown accepts no launch, task or ownership-transfer payload.");
        }

        Func<CancellationToken, Task<bool>> shutdown;
        lock (sync)
        {
            if (!ownsMutex || !callbacks.IsReady || installerShutdown is null ||
                pending is not null || restartRequested || installerShutdownPending)
            {
                throw new InvalidOperationException("The owner is busy with startup, shutdown or another lifecycle operation.");
            }
            shutdown = installerShutdown;
            installerShutdownPending = true;
        }
        try
        {
            // User/process authentication is not publisher trust. Unsigned setup still needs a native decision.
            if (!prompt.Ask(
                $"Setup at:\n{peer.Path}\n\nrequests that Kora stop before changing its installed package.\n\n" +
                "Stop Kora? The normal exit review handles unsent input and active work; you can decline that review. " +
                "Unconfirmed resource closure blocks setup. Nothing is forcibly killed. " +
                "This approves only shutdown, not installation, elevation, tasks or microphone access.",
                TimeSpan.FromSeconds(45), () => peer.IsAlive && !cancellationToken.IsCancellationRequested))
            {
                await ReplyAsync(pipe, "Declined", "Shutdown was declined, locked or expired.", cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (!peer.IsAlive || !prompt.IsEligible())
            {
                throw new InvalidOperationException("The shutdown request is no longer eligible.");
            }
            // The final proof is process exit AND clean ownership, never this acknowledgement.
            await ReplyAsync(pipe, "StopRequested", "", cancellationToken).ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(60));
            using var monitorStopping = new CancellationTokenSource();
            var monitor = MonitorInstallerEligibilityAsync(peer, timeout, monitorStopping.Token);
            bool quiescent;
            try { quiescent = await shutdown(timeout.Token).ConfigureAwait(false); }
            finally
            {
                await monitorStopping.CancelAsync().ConfigureAwait(false);
                await monitor.ConfigureAwait(false);
            }
            if (!quiescent)
            {
                await ReplyAsync(pipe, "Busy", "Active or uncertain work could not safely quiesce.", cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        finally
        {
            lock (sync) { installerShutdownPending = false; }
        }
    }

    private async Task MonitorInstallerEligibilityAsync(InstallerShutdownPeer peer,
        CancellationTokenSource quiescence, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!peer.IsAlive || !prompt.IsEligible())
                {
                    await quiescence.CancelAsync().ConfigureAwait(false);
                    return;
                }
                await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Monitor retirement does not release the host's quiescence/admission hold.
        }
    }
}
