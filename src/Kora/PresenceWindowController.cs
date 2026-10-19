using Kora.Core.Diagnostics;

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
    private bool preparing;
    private bool replacing;
    private bool retirementFailed;
    private bool stopped;
    private PresenceSwitchPhase? timedWait;

    internal bool HasPendingChange => pending;
    internal bool IsAvailable => !stopped && current is not null && !retirementFailed && !replacing;
    internal bool ActiveNative => activeNative;
    internal bool IsReadyToApply
    {
        get
        {
            if (stopped || !pending || replacing) { return false; }
            if (retirementFailed || preparing && requestedNative == activeNative) { return true; }
            if (!ownerAvailable() || !(current?.CanReplace ?? true))
            {
                MarkWait(PresenceSwitchPhase.WaitingForOwnerOrWindow);
                return false;
            }
            if (!(current?.IsReplacementReady ?? true))
            {
                MarkWait(PresenceSwitchPhase.WaitingForFade);
                return false;
            }
            timedWait = null;
            return true;
        }
    }

    private void MarkWait(PresenceSwitchPhase phase)
    {
        if (timedWait != phase) { PresenceSwitchTiming.Mark(phase); timedWait = phase; }
    }

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
        timedWait = null;
        pending = preparing || current is null || requestedNative != activeNative;
    }

    internal bool PrepareToApply()
    {
        if (!IsReadyToApply) { return false; }
        if (retirementFailed || preparing && requestedNative == activeNative) { return true; }
        preparing = true;
        return (current?.PrepareForReplacement() ?? true) && IsReadyToApply;
    }

    internal bool TryApply()
    {
        if (stopped || !pending) { return true; }
        if (retirementFailed)
        {
            pending = false;
            throw new InvalidOperationException("The previous presence window could not be retired. Exit normally before trying again.");
        }
        if (preparing && requestedNative == activeNative)
        {
            current?.CancelReplacement();
            preparing = false;
            pending = false;
            return true;
        }
        if (!PrepareToApply())
        {
            return false;
        }
        replacing = true;
        preparing = false;
        pending = false;
        var nextNative = requestedNative;
        try
        {
            if (current is not null)
            {
                retirementFailed = true;
                suspended = current.SuspendForReplacement();
                // No new window may exist until worker join and old-window teardown succeed.
                PresenceSwitchTiming.Mark(PresenceSwitchPhase.RetirementStarted);
                Retire(current);
                PresenceSwitchTiming.Mark(PresenceSwitchPhase.RetirementReturned);
                current = null;
            }
            if (stopped) { return true; }
            var state = suspended
                ?? throw new InvalidOperationException("The previous presence state is unavailable.");
            PresenceSwitchTiming.Mark(PresenceSwitchPhase.CreationStarted);
            var candidate = create(nextNative, false);
            PresenceSwitchTiming.Mark(PresenceSwitchPhase.CreationReturned);
            if (stopped)
            {
                Retire(candidate);
                return true;
            }
            try
            {
                PresenceSwitchTiming.Mark(PresenceSwitchPhase.RestoreStarted);
                candidate.RestoreAfterReplacement(state);
                PresenceSwitchTiming.Mark(PresenceSwitchPhase.RestoreReturned);
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
