using Kora.Core.Authorization;
using Kora.Core.Communication;
using Kora.Core.Hosting;

namespace Kora.Application.Communication;

// One run-owned manual layer; automatic evidence and its availability are never rewritten.
public sealed class CallCommunicationPolicy : IDisposable
{
    private readonly Lock sync = new();
    private readonly ICallStateService automatic;
    private CallPolicyObservation current;
    private bool disposed;

    public CallCommunicationPolicy(ICallStateService automatic)
    {
        this.automatic = automatic;
        current = new(0, automatic.CurrentState, false, CallAwareSettings.Default);
        automatic.StateChanged += OnAutomaticChanged;
    }

    public event EventHandler? Changed;

    public CallPolicyObservation Current
    {
        get
        {
            lock (sync)
            {
                return current;
            }
        }
    }

    public void LoadSettings(CallAwareSettings settings)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            Publish(current with { Settings = settings });
        }
    }

    public CallMutationOutcome SetManual(bool active, RequestOrigin origin, long revision, Func<bool> hostEligible)
        => CommitManual(active, origin, revision, hostEligible, static () => { }, hostEligible);

    internal CallMutationOutcome CommitManual(bool active, RequestOrigin origin, long revision,
        Func<bool> hostEligible, Action retire, Func<bool> retiredHostEligible)
    {
        lock (sync)
        {
            var denied = CheckMutation(origin, revision, hostEligible);
            if (denied is { } result) { return result; }
            if (current.ManualActive == active) { return CallMutationOutcome.Unchanged; }
            retire();
            var retiredDenial = CheckMutation(origin, revision, retiredHostEligible);
            if (retiredDenial is { } retirementDenied) { return retirementDenied; }
            return Publish(current with { ManualActive = active });
        }
    }

    public CallMutationOutcome SetSettings(CallAwareSettings settings, RequestOrigin origin, long revision,
        Func<bool> hostEligible, Func<CallAwareSettings, bool> persist)
    {
        lock (sync)
        {
            var denied = CheckMutation(origin, revision, hostEligible);
            if (denied is { } result) { return result; }
            if (settings == current.Settings) { return CallMutationOutcome.Unchanged; }
            if (!settings.ShowVisualTextDuringCalls && current.Settings.ShowVisualTextDuringCalls
                || settings.AllowVoiceActivationDuringCalls && !current.Settings.AllowVoiceActivationDuringCalls)
            {
                return CallMutationOutcome.ExactReviewUnavailable;
            }
            if (!persist(settings)) { return CallMutationOutcome.PersistenceFailed; }
            return Publish(current with { Settings = settings });
        }
    }

    public CallMutationOutcome? CheckMutation(RequestOrigin origin, long revision, Func<bool> hostEligible)
    {
        lock (sync)
        {
            if (disposed || current.ManualControlEvidenceUnavailable || !hostEligible()) { return CallMutationOutcome.HostUnavailable; }
            if (revision != current.Revision) { return CallMutationOutcome.StaleObservation; }
            if (!current.Authorization(true, true).AllowsVoiceOrCallSettings(origin))
            {
                return CallMutationOutcome.OriginDenied;
            }

            return null;
        }
    }

    internal void HoldManualControlEvidenceUnavailable()
    {
        lock (sync)
        {
            if (!disposed) { Publish(current with { ManualControlEvidenceUnavailable = true }); }
        }
    }

    internal CallMutationOutcome? CommitVoiceSetting(RequestOrigin origin, long revision,
        Func<bool> hostEligible, Action commit)
    {
        lock (sync)
        {
            var denied = CheckMutation(origin, revision, hostEligible);
            if (denied is not null) { return denied; }
            commit();
            return null;
        }
    }

    // Starting the provider inside the observation lock closes the enqueue/start race.
    public Task StartSpeech(Func<Task> start, Func<bool> hostEligible)
    {
        lock (sync)
        {
            return disposed || current.SuppressSpeech || !hostEligible() ? Task.CompletedTask : start();
        }
    }

    private void OnAutomaticChanged(object? sender, CallStateChangedEventArgs args)
    {
        lock (sync)
        {
            if (!disposed)
            {
                Publish(current with { AutomaticState = automatic.CurrentState });
            }
        }
    }

    private CallMutationOutcome Publish(CallPolicyObservation observation)
    {
        if (observation == current) { return CallMutationOutcome.Unchanged; }
        current = observation with { Revision = checked(current.Revision + 1) };
        Changed?.Invoke(this, EventArgs.Empty);
        return CallMutationOutcome.Applied;
    }

    public void Dispose()
    {
        lock (sync)
        {
            if (disposed) { return; }
            disposed = true;
            automatic.StateChanged -= OnAutomaticChanged;
            Changed = null;
        }
    }
}
