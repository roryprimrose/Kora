using Kora.Application.Communication;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class SpeechInputConfigurationService : IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly SemaphoreSlim operation = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private Task? disposal;
    private volatile bool disposed;
    private readonly ISpeechInputPreferences preferences;
    private readonly ISpeechInputProfileRuntime runtime;
    private readonly AudioControlAdmission admission;
    private readonly ISecurityAuditLog auditLog;
    private readonly ILogger<SpeechInputConfigurationService> logger;
    private SpeechInputConfigurationState state;

    public SpeechInputConfigurationService(ISpeechInputPreferences preferences, ISpeechInputProfileRuntime runtime,
        AudioControlAdmission admission, ISecurityAuditLog auditLog, ILogger<SpeechInputConfigurationService> logger)
    {
        this.preferences = preferences;
        this.runtime = runtime;
        this.admission = admission;
        this.auditLog = auditLog;
        this.logger = logger;
        try
        {
            var saved = preferences.Load();
            var desired = saved ?? SpeechInputSelection.Default;
            desired.Validate();
            runtime.InitializeProfile(desired);
            state = new(saved, desired, runtime.IsProfileAvailable ? runtime.ProfileGeneration : null, 0,
                SpeechInputProfileCatalogue.UnavailableReason(desired));
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentOutOfRangeException)
        {
            // Explicitly classified pre-host bootstrap diagnostic; no user operation or audit authority is created.
            StartupPreferenceUnavailable(exception);
            runtime.InitializeProfile(null);
            state = new(null, null, null, 0,
                "Saved speech-input state is invalid, unreadable or unconfirmed. Inspect the preference and audit evidence before repair. Typed/native input remains available; no baseline fallback was selected.", SavedStateKnown: false);
        }
    }

    public SpeechInputConfigurationState Get()
    {
        lock (gate)
        {
            return runtime.IsProfileAvailable ? state : state with
            {
                Effective = null,
                UnavailableReason = state.UnavailableReason ?? "Speech-input capture is held unavailable or switching; no microphone was opened.",
            };
        }
    }

    public async Task<bool> SelectAsync(SpeechInputSelection selection, long expectedRevision, long callRevision,
        RequestOrigin originalOrigin, SecurityAuditInitiator initiator, CallCommunicationPolicy policy,
        Func<bool> eligible, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        selection.Validate();
        originalOrigin = HostActivity.Current?.Request.Origin ?? originalOrigin;
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || originalOrigin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || initiator == SecurityAuditInitiator.VoiceCommand && originalOrigin != RequestOrigin.ActivatedVoice)
        {
            throw new InvalidOperationException("Speech-input selection requires original admitted user input.");
        }
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        cancellationToken = linked.Token;
        await operation.WaitAsync(cancellationToken).ConfigureAwait(false);
        ISpeechInputProfileSwitch? retirement = null;
        Func<HostActivity>? cutoverActivity = null;
        SecurityAuditEvent? operationAudit = null;
        Func<HostActivity>? auditContinuation = null;
        var auditTerminal = false;
        void CompletePendingAudit(SecurityAuditOutcome outcome, string reason)
        {
            if (operationAudit is null || auditTerminal || auditContinuation is null) { return; }
            using var activity = auditContinuation();
            auditLog.Write(operationAudit.WithOutcome(outcome, reason));
            auditTerminal = true;
            activity.Complete(outcome == SecurityAuditOutcome.Cancelled ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
        }
        try
        {
            SpeechInputSelection? previous;
            long nextGeneration;
            lock (gate)
            {
                if (expectedRevision != state.Revision || !eligible())
                {
                    throw new InvalidOperationException("Speech-input selection is stale or host admission changed.");
                }
                previous = state.Saved;
                nextGeneration = checked(runtime.ProfileGeneration.Number + 1);
            }
            if (await Task.Run(preferences.Load, cancellationToken).ConfigureAwait(false) != previous)
            {
                throw new InvalidDataException("Saved speech-input preference changed; restart/repair before selecting again.");
            }
            var accepted = await admission.RunObservedAsync(originalOrigin, eligible, async token =>
            {
                var request = HostActivity.RequireCurrent().Request;
                operationAudit = new(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                    "configuration.speech-input-profile", SecurityAuditOutcome.Requested,
                    request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : initiator,
                    "preferences.device-local");
                auditContinuation = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Policy);
                auditLog.Write(operationAudit);
                try
                {
                    retirement = await runtime.RetireProfileAsync(token).ConfigureAwait(false);
                    return new SpeechInputGeneration(nextGeneration, selection);
                }
                catch (OperationCanceledException)
                {
                    auditLog.Write(operationAudit.WithOutcome(SecurityAuditOutcome.Cancelled, "capture-retirement-cancelled"));
                    auditTerminal = true;
                    throw;
                }
                catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
                {
                    auditLog.Write(operationAudit.WithOutcome(SecurityAuditOutcome.Failed, "capture-retirement-unconfirmed"));
                    auditTerminal = true;
                    throw;
                }
            }, (request, _, next) =>
            {
                lock (gate)
                {
                    var audit = operationAudit ?? throw new InvalidOperationException("The speech-input request audit is missing.");
                    var denied = policy.CommitVoiceSetting(request.Origin, callRevision,
                        () => ReferenceEquals(HostActivity.RequireCurrent().Request, request)
                            && !disposed && eligible() && !cancellationToken.IsCancellationRequested && state.Revision == expectedRevision,
                        () =>
                        {
                            using var storage = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Storage);
                            try
                            {
                                if (preferences.Load() != previous)
                                {
                                    throw new InvalidDataException("Saved speech-input state changed before replacement.");
                                }
                                preferences.BeginWrite();
                                preferences.Save(selection);
                                if (preferences.ReadBack() != selection)
                                {
                                    throw new InvalidDataException("Speech-input preference readback did not match the requested tuple.");
                                }
                                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                                auditTerminal = true;
                                storage.Complete(HostOperationOutcome.Completed);
                            }
                            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
                            {
                                storage.Complete(HostOperationOutcome.Failed);
                                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed,
                                    exception is UnauthorizedAccessException ? "access-denied" : "preference-write-unconfirmed"));
                                auditTerminal = true;
                                throw;
                            }
                        });
                    if (denied is { } reason)
                    {
                        auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Denied, reason.ToString().ToLowerInvariant()));
                        auditTerminal = true;
                        return null;
                    }
                    cutoverActivity = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Runtime);
                    return next;
                }
            }, cancellationToken).ConfigureAwait(false);
            if (accepted is null) { return false; }
            using var cutover = cutoverActivity!();
            lock (gate)
            {
                if (disposed || !eligible() || cancellationToken.IsCancellationRequested || policy.CheckMutation(originalOrigin, callRevision, eligible) is not null
                    || state.Revision != expectedRevision || preferences.ReadBack() != selection)
                {
                    throw new InvalidOperationException("Speech-input write completed but cutover admission is unconfirmed. No new capture is enabled.");
                }
                var cutoverDenied = policy.CommitVoiceSetting(originalOrigin, callRevision,
                    () => !disposed && eligible() && !cancellationToken.IsCancellationRequested, () =>
                {
                    preferences.ConfirmWrite();
                    if (preferences.Load() != selection || !eligible() || cancellationToken.IsCancellationRequested
                        || policy.CheckMutation(originalOrigin, callRevision, eligible) is not null)
                    {
                        preferences.BeginWrite();
                        throw new InvalidDataException("Confirmed speech-input preference or admission changed before cutover.");
                    }
                    retirement!.Publish(accepted);
                });
                if (cutoverDenied is not null)
                {
                    preferences.BeginWrite();
                    throw new InvalidOperationException("Speech-input cutover lost call/channel admission.");
                }
                state = new(selection, selection, SpeechInputProfileCatalogue.UnavailableReason(selection) is null ? accepted : null,
                    checked(state.Revision + 1), SpeechInputProfileCatalogue.UnavailableReason(selection));
                SelectionCommitted(accepted.Number, state.Effective is not null);
                cutover.Complete(HostOperationOutcome.Completed);
            }
            return true;
        }
        catch (OperationCanceledException)
        {
            retirement?.HoldUnavailable();
            if (retirement is not null || !runtime.IsProfileAvailable)
            { HoldUnavailable("Speech-input switching was cancelled. Capture remains stopped; inspect saved state before retry."); }
            SelectionCancelled();
            CompletePendingAudit(SecurityAuditOutcome.Cancelled, "input-cancelled-before-write");
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            retirement?.HoldUnavailable();
            if (retirement is not null || !runtime.IsProfileAvailable)
            { HoldUnavailable("Speech-input change failed or its evidence is unconfirmed. No new capture is enabled. Inspect saved state and audit; typed/native input remains available."); }
            SelectionFailed(exception);
            CompletePendingAudit(SecurityAuditOutcome.Failed, "input-or-evidence-unavailable");
            throw;
        }
        finally
        {
            try
            {
                if (retirement is not null) { await retirement.DisposeAsync().ConfigureAwait(false); }
            }
            finally { operation.Release(); }
        }
    }

    private void HoldUnavailable(string reason)
    {
        lock (gate) { state = state with { Saved = null, Desired = null, SavedStateKnown = false,
            Effective = null, Revision = checked(state.Revision + 1), UnavailableReason = reason }; }
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            disposed = true;
            return new(disposal ??= DisposeCoreAsync());
        }
    }

    private async Task DisposeCoreAsync()
    {
        await lifetime.CancelAsync().ConfigureAwait(false);
        await operation.WaitAsync().ConfigureAwait(false);
        operation.Release();
        operation.Dispose();
        lifetime.Dispose();
    }
}
