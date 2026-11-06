using Kora.Core.Authorization;
using Kora.Core.Communication;
using Kora.Core.Hosting;

namespace Kora.Application.Communication;

// One run-owned manual layer; automatic evidence and its availability are never rewritten.
public sealed class CallCommunicationPolicy : IDisposable, IHostAuthorizationPolicyGate
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
        lock (sync) { SynchronizeAutomatic(); }
    }

    public event EventHandler? Changed;

    // Presentation reads an immutable observation; consequential admission still takes the commit lock.
    public CallPolicyObservation Current => Volatile.Read(ref current);

    public T WithCurrent<T>(HostAuthorizationPolicy baseline, Func<HostAuthorizationPolicy, T> commit)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            SynchronizeAutomatic();
            return commit(current.Authorization(baseline.IsUnlocked, baseline.OtherMandatoryGatesSatisfied));
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
        Func<bool> hostEligible, Func<CallAwareSettings, bool> persist, bool spokenApprovalDowngradeConfirmed = false)
        => SetSettingsCore(settings, origin, revision, hostEligible, persist, spokenApprovalDowngradeConfirmed);

    internal CallVoiceActivationReview ReviewVoiceActivation(RequestOrigin origin, long revision,
        CallAwareSettings? savedSettings, Func<bool> eligible)
    {
        lock (sync)
        {
            if (origin != RequestOrigin.LocalUi || CheckMutation(origin, revision, eligible) is not null
                || current.Settings.AllowVoiceActivationDuringCalls
                || (savedSettings ?? CallAwareSettings.Default) != current.Settings)
            {
                throw new InvalidOperationException("The exact call activation preference cannot be reviewed. Start a fresh native review.");
            }
            return new(this, current, savedSettings, eligible);
        }
    }

    internal CallMutationOutcome SetReviewedVoiceActivation(CallVoiceActivationReview review, RequestOrigin origin,
        Func<CallAwareSettings, bool> persist) =>
        SetSettingsCore(review.Observation.Settings with { AllowVoiceActivationDuringCalls = true }, origin,
            review.Observation.Revision, () => review.IsEligible, persist, activationReview: review);

    private CallMutationOutcome SetSettingsCore(CallAwareSettings settings, RequestOrigin origin, long revision,
        Func<bool> hostEligible, Func<CallAwareSettings, bool> persist, bool spokenApprovalDowngradeConfirmed = false,
        CallVoiceActivationReview? activationReview = null)
    {
        lock (sync)
        {
            if (activationReview is not null
                && (!activationReview.TryConsume(this, settings) || origin != RequestOrigin.LocalUi))
            { return CallMutationOutcome.ExactReviewUnavailable; }
            var denied = CheckMutation(origin, revision, hostEligible);
            if (denied is { } result) { return result; }
            if (settings == current.Settings) { return CallMutationOutcome.Unchanged; }
            if (!settings.ShowVisualTextDuringCalls && current.Settings.ShowVisualTextDuringCalls
                || settings.AllowVoiceActivationDuringCalls && !current.Settings.AllowVoiceActivationDuringCalls
                    && activationReview is null
                || settings.AllowSpokenApprovalsDuringCalls && !current.Settings.AllowSpokenApprovalsDuringCalls
                    && (origin != RequestOrigin.LocalUi || !spokenApprovalDowngradeConfirmed))
            {
                return CallMutationOutcome.ExactReviewUnavailable;
            }
            if (!persist(settings)) { return CallMutationOutcome.PersistenceFailed; }
            if (activationReview is not null && CheckMutation(origin, revision, hostEligible) is { } invalidated)
            { return invalidated; }
            return Publish(current with { Settings = settings });
        }
    }

    public CallMutationOutcome? CheckMutation(RequestOrigin origin, long revision, Func<bool> hostEligible)
    {
        lock (sync)
        {
            if (!disposed) { SynchronizeAutomatic(); }
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
            if (!disposed) { SynchronizeAutomatic(); }
            return disposed || current.SuppressSpeech || !hostEligible() ? Task.CompletedTask : start();
        }
    }

    internal Task StartReviewedSpeech(CallSpeechLease lease, string text, Func<Task> start, Func<bool> hostEligible)
    {
        lock (sync)
        {
            if (!disposed) { SynchronizeAutomatic(); }
            return disposed || !lease.TryStart(this, text) || !hostEligible() ? Task.CompletedTask : start();
        }
    }

    private void OnAutomaticChanged(object? sender, CallStateChangedEventArgs args)
    {
        lock (sync)
        {
            if (!disposed)
            {
                SynchronizeAutomatic();
            }
        }
    }

    private void SynchronizeAutomatic() => Publish(current with { AutomaticState = automatic.CurrentState });

    private CallMutationOutcome Publish(CallPolicyObservation observation)
    {
        if (observation == current) { return CallMutationOutcome.Unchanged; }
        Volatile.Write(ref current, observation with { Revision = checked(current.Revision + 1) });
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