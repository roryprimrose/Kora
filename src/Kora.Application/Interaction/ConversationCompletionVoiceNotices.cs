using Kora.Application.Communication;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Voice;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Interaction;

internal sealed partial class ConversationCompletionVoiceNotices : IAsyncDisposable
{
    private readonly LocalEventBroker broker;
    private readonly CallCommunicationPolicy calls;
    private readonly ISpeechPlaybackService speech;
    private readonly ISecurityAuditLog audit;
    private readonly ILogger logger;
    internal ConversationCompletionVoiceNotices(LocalEventBroker broker, CallCommunicationPolicy calls,
        ISpeechPlaybackService speech, ISecurityAuditLog audit, ILogger logger)
    {
        this.broker = broker;
        this.calls = calls;
        this.speech = speech;
        this.audit = audit;
        this.logger = logger;
        broker.NoticeAdmissionChanged += InvalidateDelivery;
    }

    internal enum DeliveryOutcome { NotRequested, CapacityHeld, Skipped, QueuedNotDelivered, Completed, CancelledJoined, Unconfirmed }
    internal const string Statement = "Your conversation task is complete.";
    internal const string Disclosure = "Off by default, independent of ordinary voice feedback. Enable only for an explicitly active voice session this run. "
        + "New successful ordinary conversation completions may say only 'Your conversation task is complete.' once, during a conversational gap. "
        + "No task title, answer, question, microphone activation or focus change. Visual notices remain. "
        + "Quiet, calls, capture, prompts, changed input or private ownership suppress without replay. "
        + "Disable listening, lock, privacy loss or restart turns this choice Off; unlock never restores it.";
    private readonly Lock sync = new();
    private readonly HashSet<Guid> observed = [];
    private Consent? consent;
    private CancellationTokenSource? active;
    private TaskCompletionSource joined = CompletedJoin();
    private Func<bool> deliveryEligible = static () => false;
    private long revision;
    private bool closed;
    private bool held;
    internal DeliveryOutcome Outcome { get; private set; }
    internal bool Enabled { get { lock (sync) { return consent is not null && !held && !closed; } } }
    internal long Revision { get { lock (sync) { return revision; } } }
    private sealed record Consent(HostId<SessionIdentity> Session, HostRevision Generation, Func<bool> VoiceCurrent);

    internal void Revalidate()
    {
        lock (sync)
        {
            if (consent is { } current && !current.VoiceCurrent()) { consent = null; revision++; }
            if (active is not null && (consent is null || !deliveryEligible()))
            {
                active.Cancel();
                speech.InvalidateOutput();
            }
        }
    }

    internal void Retire()
    {
        lock (sync)
        {
            consent = null;
            revision++;
            if (active is not null) { active.Cancel(); speech.InvalidateOutput(); }
        }
    }

    private void InvalidateDelivery()
    {
        lock (sync)
        {
            if (active is not null) { active.Cancel(); speech.InvalidateOutput(); }
        }
    }

    internal async Task ChangeNativeAsync(HostId<SessionIdentity> session, HostRevision generation, bool enabled,
        long expectedRevision, Func<bool> voiceCurrent, Func<bool> nativeCurrent, CancellationToken token)
    {
        var host = HostActivity.RequireCurrent();
        if (host.Request.Origin != RequestOrigin.LocalUi || host.Request.SessionId != session || host.Activity!.IsStopped)
        { throw new InvalidOperationException("Completion voice notices require original exact native session input."); }
        SecurityAuditEvent? appliedAudit = null;
        try
        {
            if (enabled)
            {
                await broker.WithCompletionConsentSourcesAsync(session, Apply,
                    () => !closed && !held && nativeCurrent() && !host.Activity.IsStopped, token).ConfigureAwait(false);
            }
            else { Apply([]); }
        }
        catch (Exception exception)
        {
            lock (sync) { held = true; Retire(); }
            Failure(logger, exception.GetType().Name);
            if (appliedAudit is { } unconfirmed)
            {
                using var failedAudit = HostActivity.BeginAudit(host.Request, unconfirmed);
                audit.Write(unconfirmed.WithOutcome(SecurityAuditOutcome.Failed, "completion-consent-source-unconfirmed"));
                failedAudit.Complete(HostOperationOutcome.Failed);
            }
            throw;
        }

        void Apply(IReadOnlyList<LocalEvent> existing)
        {
            lock (sync)
            {
                if (closed || held || revision != expectedRevision || !nativeCurrent())
                { throw new InvalidOperationException("Completion notice consent changed or is unavailable."); }
                if (!enabled) { Retire(); }
                var record = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
                    enabled ? "completion-notice.voice.enable" : "completion-notice.voice.off",
                    SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "completion-notice.voice.run");
                using var activity = HostActivity.BeginAudit(host.Request, record);
                var requested = false;
                try
                {
                    audit.Write(record);
                    requested = true;
                    token.ThrowIfCancellationRequested();
                    if (!nativeCurrent() || enabled && (!voiceCurrent() || calls.Current.IsProtected || broker.RoutineQuiet.Enabled))
                    { throw new InvalidOperationException("An explicitly active current-run voice session and clear native admission are required."); }
                    foreach (var item in existing.Where(item => item.Source == LocalEventSource.ConversationTask
                        && item.Type == LocalEventType.Completed))
                    {
                        if (observed.Count == LocalEventBrokerState.MaximumReceipts && !observed.Contains(item.Id))
                        { throw new InvalidOperationException("Completion speech suppression capacity is full."); }
                        observed.Add(item.Id);
                    }
                    audit.Write(record.WithOutcome(SecurityAuditOutcome.Succeeded));
                    token.ThrowIfCancellationRequested();
                    if (!nativeCurrent() || enabled && !voiceCurrent())
                    { throw new InvalidOperationException("Original voice-session consent retired before application."); }
                    consent = enabled ? new(session, generation, voiceCurrent) : null;
                    revision++;
                    appliedAudit = record;
                    activity.Complete(HostOperationOutcome.Completed);
                    return;
                }
                catch (Exception exception)
                {
                    consent = null;
                    held = true;
                    Failure(logger, exception.GetType().Name);
                    if (requested)
                    {
                        audit.Write(record.WithOutcome(exception is OperationCanceledException
                        ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed, "completion-consent-unconfirmed"));
                    }
                    activity.Complete(HostOperationOutcome.Failed);
                    throw;
                }
            }
        }
    }

    internal async Task DeliverAsync(LocalEventSnapshot snapshot, Func<bool> hostCurrent,
        SpeechVoice? voice, AudioOutputDevice? output, CancellationToken token)
    {
        LocalEvent? target = null;
        Consent? choice;
        CancellationTokenSource cancellation;
        CancellationTokenSource expiry;
        TaskCompletionSource completion;
        lock (sync)
        {
            foreach (var view in snapshot.Events.Where(view => view.Event.Source == LocalEventSource.ConversationTask
                && view.Event.Type == LocalEventType.Completed))
            {
                if (observed.Count == LocalEventBrokerState.MaximumReceipts && !observed.Contains(view.Event.Id))
                { held = true; Outcome = DeliveryOutcome.CapacityHeld; Retire(); return; }
                if (observed.Add(view.Event.Id) && view.Reason == LocalEventReason.Eligible) { target ??= view.Event; }
            }
            choice = consent;
            if (target is null || choice is null || closed || held || active is not null
                || voice is null || output is null || choice.Session != target.SessionId || choice.Generation.Value != target.Generation)
            { return; }
            var remaining = target.ExpiresAt - broker.CompletionSpeechClock.GetUtcNow();
            if (remaining <= TimeSpan.Zero) { Outcome = DeliveryOutcome.Skipped; return; }
            expiry = new(remaining, broker.CompletionSpeechClock);
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, expiry.Token);
            active = cancellation;
            completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            joined = completion;
            var admittedRevision = revision;
            deliveryEligible = () => !closed && !held && !cancellation.IsCancellationRequested
                && revision == admittedRevision && ReferenceEquals(consent, choice) && choice.VoiceCurrent()
                && !calls.Current.IsProtected && !calls.Current.SuppressSpeech && !broker.RoutineQuiet.Enabled
                && broker.IsCurrent(snapshot) && hostCurrent();
        }
        var request = new HostRequest(new(Guid.NewGuid()), target.SessionId, target.TaskId!.Value, RequestOrigin.HostSystem);
        var links = HostActivity.Current?.Activity is { } cause ? new System.Diagnostics.ActivityLink[] { new(cause.Context) } : [];
        using var operation = HostActivity.BeginRoot(request, HostActivityLayer.Application, HostOperation.Presentation, links);
        var record = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ApplicationExecution,
            "completion-notice.voice", SecurityAuditOutcome.Requested, SecurityAuditInitiator.System,
            target.Id.ToString("D"));
        var requested = false;
        var started = false;
        try
        {
            bool Eligible() { lock (sync) { return deliveryEligible(); } }
            if (!Eligible()) { Outcome = DeliveryOutcome.Skipped; operation.Complete(HostOperationOutcome.Cancelled); return; }
            using var auditActivity = HostActivity.BeginAudit(request, record);
            audit.Write(record);
            requested = true;
            Outcome = DeliveryOutcome.QueuedNotDelivered;
            await speech.SpeakAdmittedAsync(Statement, voice, output, async (start, startToken) =>
            {
                await broker.StartCompletionSpeechAsync(snapshot, target, Eligible, () =>
                {
                    // This is the ordinary central gate, never a protected-call one-shot exception.
                    _ = calls.StartSpeech(() =>
                    {
                        startToken.ThrowIfCancellationRequested();
                        start();
                        started = true;
                        return Task.CompletedTask;
                    }, Eligible);
                }, startToken).ConfigureAwait(false);
            }, cancellation.Token).ConfigureAwait(false);
            if (speech.IsSpeaking) { throw new InvalidOperationException("Owned completion output has not joined."); }
            Outcome = started && Eligible() ? DeliveryOutcome.Completed : started ? DeliveryOutcome.CancelledJoined : DeliveryOutcome.Skipped;
            audit.Write(record.WithOutcome(Outcome == DeliveryOutcome.Completed ? SecurityAuditOutcome.Succeeded : SecurityAuditOutcome.Cancelled,
                Outcome == DeliveryOutcome.Completed ? "completion-speech-joined" : "completion-speech-suppressed"));
            auditActivity.Complete(Outcome == DeliveryOutcome.Completed ? HostOperationOutcome.Completed : HostOperationOutcome.Cancelled);
            operation.Complete(Outcome == DeliveryOutcome.Completed ? HostOperationOutcome.Completed : HostOperationOutcome.Cancelled);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            var cancelled = exception is OperationCanceledException && !speech.IsSpeaking;
            Outcome = cancelled ? started ? DeliveryOutcome.CancelledJoined : DeliveryOutcome.Skipped : DeliveryOutcome.Unconfirmed;
            if (!cancelled) { lock (sync) { held = true; consent = null; } }
            Failure(logger, exception.GetType().Name);
            if (requested)
            {
                using var failedAudit = HostActivity.BeginAudit(request, record);
                audit.Write(record.WithOutcome(cancelled ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed,
                    cancelled ? "completion-speech-cancelled-joined" : "completion-speech-unconfirmed"));
                failedAudit.Complete(cancelled ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            }
            operation.Complete(cancelled ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        finally
        {
            lock (sync)
            {
                active = null;
                deliveryEligible = static () => false;
                cancellation.Dispose();
                expiry.Dispose();
                completion.TrySetResult();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        Task join;
        lock (sync) { closed = true; Retire(); broker.NoticeAdmissionChanged -= InvalidateDelivery; join = joined.Task; }
#pragma warning disable VSTHRD003 // Joins this owner's exact physical output operation; cancellation is not completion.
        await join.ConfigureAwait(false);
#pragma warning restore VSTHRD003
    }

    internal Task RetireAndJoinAsync()
    {
#pragma warning disable VSTHRD003 // Returns the join of this owner's existing output, not unrelated caller work.
        lock (sync) { Retire(); return joined.Task; }
#pragma warning restore VSTHRD003
    }

    private static TaskCompletionSource CompletedJoin()
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        completion.SetResult();
        return completion;
    }
}