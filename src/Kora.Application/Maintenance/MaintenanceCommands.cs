using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Maintenance;
using Kora.Core.Storage;

namespace Kora.Application.Maintenance;

public sealed class MaintenanceCommands(MaintenanceViewModel state, ISessionWorkspaceStore workspace,
    IMaintenanceControlSessionStore sessions, HostTaskCoordinator tasks) : IMaintenanceCommands, IAsyncDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private readonly Lock disposalGate = new();
    private Task? disposal;
    private WorkSessionAuthorization? session;
    private bool unavailable;
    private bool disposed;

    public async Task<string> ExecuteAsync(MaintenanceCommand command, RequestOrigin origin,
        Func<bool> eligible, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || HostActivity.Current is not null || command is not (MaintenanceCommand.Status or MaintenanceCommand.Review or MaintenanceCommand.Snooze))
        {
            throw new InvalidOperationException("Cached maintenance requires new exact original user input, not ambient or model authority.");
        }
        var target = state.CaptureCachedTarget();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        var token = linked.Token;
        await gate.WaitAsync(token).ConfigureAwait(false);
        string output;
        try
        {
            bool Admitted() => !disposed && !unavailable && eligible();
            if (!Admitted()) { throw new InvalidOperationException("Maintenance host admission is unavailable."); }
            if (session is null)
            {
                var creation = HostRequest.Create(origin);
                using var activity = HostActivity.BeginRoot(creation, HostActivityLayer.Application, HostOperation.Policy);
                var intent = await workspace.RecordControlIntentAsync(creation, token).ConfigureAwait(false);
                try
                {
                    session = await sessions.CreateMaintenanceControlSessionAsync(creation, Admitted, token).ConfigureAwait(false);
                    await tasks.RecordOutcomeAsync(intent, HostTaskState.Succeeded, CancellationToken.None).ConfigureAwait(false);
                }
                catch { unavailable = true; throw; }
                activity.Complete(HostOperationOutcome.Completed);
            }
            var request = new HostRequest(new(Guid.NewGuid()), session.SessionId, new(Guid.NewGuid()), origin);
            using var operation = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Request);
            var admitted = await workspace.RecordControlIntentAsync(request, token).ConfigureAwait(false);
            var terminalRecorded = false;
            try
            {
                output = await sessions.WithMaintenanceControlSessionAsync(request, session.Generation, () =>
                {
                    token.ThrowIfCancellationRequested();
                    if (!Admitted() || !ReferenceEquals(HostActivity.RequireCurrent().Request, request))
                    {
                        throw new InvalidOperationException("Maintenance original channel, ownership, privacy or call admission changed.");
                    }
                    return state.ApplyCached(command, target, request, Admitted, token);
                }, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (!Admitted() || !state.IsCachedTargetCurrent(target, command))
                {
                    throw new InvalidOperationException("Maintenance admission or exact cached target changed before receipt.");
                }
                try { await tasks.RecordOutcomeAsync(admitted, HostTaskState.Succeeded, CancellationToken.None).ConfigureAwait(false); }
                catch { unavailable = true; throw; }
                terminalRecorded = true;
                token.ThrowIfCancellationRequested();
                if (!Admitted() || !state.IsCachedTargetCurrent(target, command))
                {
                    throw new InvalidOperationException("Maintenance receipt returned after admission or cache changed; no late success is presented.");
                }
                operation.Complete(HostOperationOutcome.Completed);
            }
            catch (Exception exception)
            {
                operation.Complete(exception is OperationCanceledException
                    ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
                if (!terminalRecorded)
                {
                    await tasks.RecordOutcomeAsync(admitted, HostTaskState.Failed, CancellationToken.None).ConfigureAwait(false);
                }
                throw;
            }
        }
        finally { gate.Release(); }
        return output;
    }

    public ValueTask DisposeAsync()
    {
        lock (disposalGate) { return new(disposal ??= CloseAsync()); }
    }

    private async Task CloseAsync()
    {
        disposed = true;
        await lifetime.CancelAsync().ConfigureAwait(false);
        await gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        gate.Dispose();
        lifetime.Dispose();
    }
}
