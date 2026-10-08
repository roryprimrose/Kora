using Kora.Application.Communication;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Communication;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class InCallFeedbackConfigurationService(
    IInCallFeedbackPreferences preferences, AudioControlAdmission admission,
    ISecurityAuditLog auditLog, ISpeechPlaybackService playback, ILogger<InCallFeedbackConfigurationService> logger)
{
    private readonly Lock gate = new();
    private IReadOnlyList<InCallFeedbackChoice> choices = [];
    private InCallFeedbackMode? saved;
    private InCallFeedbackMode? desired;
    private long revision;
    private bool applying;
    private bool held = true;
    private string? recovery = "Inspect call feedback in the current owning unlocked host.";

    public event EventHandler? Changed;
    public IReadOnlyList<InCallFeedbackChoice> Choices { get { lock (gate) { return choices; } } }

    public InCallFeedbackCommandResult Get(long callRevision = 0, string outcome = "observed")
    {
        lock (gate)
        {
            return new(outcome, recovery, revision, callRevision, saved, desired,
                held ? "unavailable" : saved is null ? "default" : "saved", !held);
        }
    }

    private InCallFeedbackMode? Read(bool readBack = false)
    {
        var value = readBack ? preferences.ReadBack() : preferences.Load();
        if (value is { } mode && !Enum.IsDefined(mode)) { throw new InvalidDataException("Saved call feedback is invalid."); }
        return value;
    }

    public void Observe()
    {
        lock (gate)
        {
            if (applying) { throw new InvalidOperationException("Call feedback is committing or notifying."); }
            try
            {
                var value = Read();
                if (!held && saved == value) { return; }
                playback.InvalidateOutput();
                saved = value;
                desired = value ?? InCallFeedbackRules.Default;
                held = false;
                recovery = null;
                revision = checked(revision + 1);
                choices = [];
                Notify();
            }
            catch (Exception exception) { PreferenceUnavailable(logger, exception); HoldUnavailable(); throw; }
        }
    }

    public async Task RefreshAsync(RequestOrigin origin, long callRevision, Func<bool> eligible, CancellationToken token)
    {
        var entered = false;
        var expectedRevision = 0L;
        try
        {
            await admission.RunAsync(origin, eligible, (request, authority) =>
            {
                lock (gate)
                {
                    if (applying) { throw new InvalidOperationException("Call feedback is committing or notifying."); }
                    applying = true;
                    entered = true;
                    var value = Read();
                    held = true;
                    playback.InvalidateOutput();
                    saved = value;
                    desired = value ?? InCallFeedbackRules.Default;
                    recovery = null;
                    revision = checked(revision + 1);
                    expectedRevision = revision;
                    choices = Array.AsReadOnly(Enum.GetValues<InCallFeedbackMode>()
                        .Select(mode => new InCallFeedbackChoice(mode, revision, callRevision, authority, request.Origin, eligible)).ToArray());
                    return true;
                }
            }, token).ConfigureAwait(false);
            lock (gate)
            {
                token.ThrowIfCancellationRequested();
                if (revision != expectedRevision || !eligible() || Read() != saved)
                {
                    throw new InvalidOperationException("Call feedback discovery admission or saved state changed before its receipt.");
                }
                held = false;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception exception)
        {
            PreferenceUnavailable(logger, exception);
            if (entered) { HoldUnavailable(); }
            throw;
        }
        finally { if (entered) { lock (gate) { applying = false; } } }
    }

    public async Task<bool> SelectAsync(InCallFeedbackChoice choice, bool reset, long callRevision, RequestOrigin origin,
        SecurityAuditInitiator initiator, CallCommunicationPolicy policy, Func<bool> eligible, CancellationToken token)
    {
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || initiator == SecurityAuditInitiator.VoiceCommand && origin != RequestOrigin.ActivatedVoice)
        {
            throw new InvalidOperationException("Call feedback requires original user input provenance.");
        }
        var entered = false;
        var expectedRevision = 0L;
        var target = reset ? (InCallFeedbackMode?)null : choice.Mode;
        try
        {
            var accepted = await admission.RunAsync(origin, eligible, (request, authority) =>
            {
                lock (gate)
                {
                    if (held || applying || choice.Session != authority || choice.Revision != revision
                        || choice.CallRevision != callRevision || choice.Origin != request.Origin || !choice.Eligible()
                        || !choices.Any(item => ReferenceEquals(item, choice)) || Read() != saved
                        || reset && choice.Mode != InCallFeedbackRules.Default)
                    {
                        throw new InvalidOperationException("Call feedback choice, saved state or original session/generation changed. Inspect and choose again.");
                    }
                    applying = true;
                    entered = true;
                    var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                        "configuration.in-call-feedback", SecurityAuditOutcome.Requested,
                        request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : initiator,
                        "preferences.device-local");
                    auditLog.Write(audit);
                    var denied = policy.CommitVoiceSetting(request.Origin, callRevision,
                        () => ReferenceEquals(HostActivity.RequireCurrent().Request, request) && eligible() && !token.IsCancellationRequested,
                        () =>
                        {
                            held = true;
                            playback.InvalidateOutput();
                            try
                            {
                                preferences.BeginWrite();
                                if (reset) { preferences.Reset(); } else { preferences.Save(choice.Mode); }
                                if (Read(readBack: true) != target) { throw new InvalidDataException("Saved call feedback does not match the exact selection."); }
                            }
                            catch (Exception exception)
                            {
                                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed,
                                    exception is InvalidDataException ? "invalid-data" : exception is IOException ? "io-error"
                                    : exception is UnauthorizedAccessException ? "access-denied" : "operation-failed"));
                                throw;
                            }
                            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                            saved = target;
                            desired = target ?? InCallFeedbackRules.Default;
                            revision = checked(revision + 1);
                            expectedRevision = revision;
                            choices = [];
                        });
                    if (denied is { } reason)
                    {
                        auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Denied, reason.ToString().ToLowerInvariant()));
                        return false;
                    }
                    return true;
                }
            }, token).ConfigureAwait(false);
            if (accepted)
            {
                lock (gate)
                {
                    token.ThrowIfCancellationRequested();
                    if (revision != expectedRevision || !choice.Eligible())
                    {
                        throw new InvalidOperationException("Call feedback revision or original admission changed before its apply receipt.");
                    }
                    var denied = policy.CommitVoiceSetting(origin, callRevision, eligible, () =>
                    {
                        if (Read(readBack: true) != target) { throw new InvalidDataException("Saved call feedback changed before its completed receipt."); }
                        preferences.ConfirmWrite();
                        try
                        {
                            token.ThrowIfCancellationRequested();
                            if (Read() != target || !eligible() || policy.Current.Revision != callRevision)
                            {
                                throw new InvalidDataException("Confirmed call feedback or admission changed before activation.");
                            }
                            held = false;
                            recovery = null;
                            Changed?.Invoke(this, EventArgs.Empty);
                            if (revision != expectedRevision || !eligible() || policy.Current.Revision != callRevision)
                            {
                                throw new InvalidOperationException("Call feedback admission changed during confirmation notification.");
                            }
                        }
                        catch { preferences.BeginWrite(); throw; }
                    });
                    if (denied is not null) { throw new InvalidOperationException("Call feedback admission changed before evidence confirmation."); }
                }
            }
            return accepted;
        }
        catch (Exception exception)
        {
            PreferenceUnavailable(logger, exception);
            if (entered) { HoldUnavailable(); }
            throw;
        }
        finally { if (entered) { lock (gate) { applying = false; } } }
    }

    public void HoldUnavailable()
    {
        lock (gate)
        {
            held = true;
            recovery = "Call feedback or admission/evidence is unconfirmed. Inspect saved state and audit/intent receipts before explicit repair and refresh; full visual remains, no automatic retry.";
            choices = [];
            revision = checked(revision + 1);
            try { playback.InvalidateOutput(); }
            finally { Notify(); }
        }
    }

    private void Notify()
    {
        var previous = applying;
        applying = true;
        try { Changed?.Invoke(this, EventArgs.Empty); }
        finally { applying = previous; }
    }
}
