using Kora.Core.Coordination;

namespace Kora;

public sealed class DesktopInstanceOwnershipBridge : IInstanceHostCallbacks
{
    private readonly Lock sync = new();
    private Func<CancellationToken, Task>? activate;
    private Func<CancellationToken, Task<bool>>? quiesce;
    private Func<CancellationToken, Task>? abort;
    private bool quiescenceActive;
    private bool abortFailed;

    public bool IsCapabilityAdmissionOpen
    {
        get
        {
            lock (sync)
            {
                return activate is not null && quiesce is not null
                    && !quiescenceActive && !abortFailed;
            }
        }
    }

    public bool IsReady
    {
        get
        {
            lock (sync)
            {
                return activate is not null && quiesce is not null;
            }
        }
    }

    public void BindCallbacks(
        Func<CancellationToken, Task> activateExisting,
        Func<CancellationToken, Task<bool>> quiesceForHandoff,
        Func<CancellationToken, Task>? abortHandoff = null)
    {
        ArgumentNullException.ThrowIfNull(activateExisting);
        ArgumentNullException.ThrowIfNull(quiesceForHandoff);
        lock (sync)
        {
            activate = activateExisting;
            quiesce = quiesceForHandoff;
            abort = abortHandoff;
        }
    }

    public void UnbindCallbacks()
    {
        lock (sync)
        {
            activate = null;
            quiesce = null;
            abort = null;
        }
    }

    public Task ActivateExistingAsync(CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task>? callback;
        lock (sync)
        {
            callback = activate;
        }

        return callback is null
            ? Task.FromException(new InvalidOperationException("The desktop is not ready."))
            : callback(cancellationToken);
    }

    public bool IsHandoffRecoveryRequired
    {
        get
        {
            lock (sync)
            {
                return abortFailed;
            }
        }
    }

    public async Task<bool> QuiesceForHandoffAsync(CancellationToken cancellationToken)
    {
        Func<CancellationToken, Task<bool>>? callback;
        Func<CancellationToken, Task>? abortCallback;
        lock (sync)
        {
            // Never start irreversible preparation without a reversible admission-only abort.
            if (quiescenceActive || abortFailed || quiesce is null || abort is null)
            {
                return false;
            }

            callback = quiesce;
            abortCallback = abort;
            quiescenceActive = true;
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var prepared = await callback(cancellationToken).ConfigureAwait(false);
            if (!prepared)
            {
                await TryAbortPreparationAsync(abortCallback).ConfigureAwait(false);
            }

            return prepared;
        }
        catch
        {
            // Await the actual callback before undoing its hold, even when the IPC waiter timed out.
            await TryAbortPreparationAsync(abortCallback).ConfigureAwait(false);
            throw;
        }
        finally
        {
            lock (sync)
            {
                quiescenceActive = false;
            }
        }
    }

    private async Task TryAbortPreparationAsync(Func<CancellationToken, Task> abortCallback)
    {
        try
        {
            // Approval cancellation cannot cancel restoration of admission. Never restore audio/grants.
            await abortCallback(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            lock (sync)
            {
                abortFailed = true;
            }
        }
    }
}
