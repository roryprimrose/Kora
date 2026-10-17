namespace Kora;

internal sealed class PresenceWindowController(
    Func<bool, bool, IPresenceWindow> create,
    Func<bool> ownerAvailable)
{
    private IPresenceWindow? current;
    private PresenceWindowState? suspended;
    private bool activeNative;
    private bool requestedNative;
    private bool pending;
    private bool replacing;
    private bool retirementFailed;
    private bool stopped;

    internal bool HasPendingChange => pending;
    internal bool IsAvailable => !stopped && current is not null && !retirementFailed && !replacing;
    internal bool ActiveNative => activeNative;
    internal bool IsReadyToApply => !stopped && pending && !replacing
        && (retirementFailed || ownerAvailable() && (current?.CanReplace ?? true));

    internal void Start(bool useNative)
    {
        if (current is not null || stopped)
        {
            throw new InvalidOperationException("The presence window controller has already started or stopped.");
        }
        current = create(useNative, true);
        activeNative = requestedNative = useNative;
    }

    internal void Request(bool useNative)
    {
        if (stopped) { return; }
        requestedNative = useNative;
        pending = current is null || requestedNative != activeNative;
    }

    internal bool TryApply()
    {
        if (stopped || !pending) { return true; }
        if (retirementFailed)
        {
            pending = false;
            throw new InvalidOperationException("The previous presence window could not be retired. Exit normally before trying again.");
        }
        if (replacing || !ownerAvailable() || current is { CanReplace: false })
        {
            return false;
        }
        replacing = true;
        pending = false;
        var nextNative = requestedNative;
        try
        {
            if (current is not null)
            {
                retirementFailed = true;
                suspended = current.SuspendForReplacement();
                // No new window may exist until worker join and old-window teardown succeed.
                Retire(current);
                current = null;
            }
            if (stopped) { return true; }
            var state = suspended
                ?? throw new InvalidOperationException("The previous presence state is unavailable.");
            var candidate = create(nextNative, false);
            if (stopped)
            {
                Retire(candidate);
                return true;
            }
            try
            {
                candidate.RestoreAfterReplacement(state);
            }
            catch (Exception failure) when (failure is not OutOfMemoryException)
            {
                try { Retire(candidate); }
                catch (Exception cleanup) when (cleanup is not OutOfMemoryException)
                {
                    throw new AggregateException("The replacement presence failed and could not be retired.", failure, cleanup);
                }
                throw;
            }
            if (stopped)
            {
                Retire(candidate);
                return true;
            }
            current = candidate;
            activeNative = nextNative;
            suspended = null;
            pending = requestedNative != activeNative;
            return true;
        }
        finally
        {
            replacing = false;
        }
    }

    private void Retire(IPresenceWindow window)
    {
        retirementFailed = true;
        window.CloseForReplacement();
        retirementFailed = false;
    }

    internal void Stop()
    {
        stopped = true;
        pending = false;
    }
}
