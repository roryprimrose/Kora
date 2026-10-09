using Kora.Application.Communication;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed class ResponseModeConfigurationService(
    IResponseOutputPreferences preferences, AudioControlAdmission admission,
    ISecurityAuditLog auditLog, ISpeechPlaybackService playback)
{
    private readonly Lock gate = new();
    private readonly Guid owner = Guid.NewGuid();
    private IReadOnlyList<ResponseModeChoice> choices = [];
    private ResponseOutputMode? saved;
    private ResponseOutputMode? desired;
    private long revision;
    private bool applying;
    private bool held = true;
    private string? recovery = "Inspect response preferences in the current owning unlocked host.";

    public event EventHandler? Changed;
    public IReadOnlyList<ResponseModeChoice> Choices { get { lock (gate) { return choices; } } }

    public ResponseModeCommandResult Get(long callRevision = 0, string outcome = "observed",
        ResponseOutputMode? queueOverride = null, ResponseOutputMode? taskOverride = null)
    {
        lock (gate)
        {
            return new(outcome, recovery, revision, callRevision, saved, desired,
                !held && desired is { } mode ? ResponseOutputModeResolver.Resolve(mode, queueOverride, taskOverride) : null,
                held ? "unavailable" : saved is null ? "default" : "saved", !held);
        }
    }

    private ResponseOutputMode? Read(bool readBack = false)
    {
        var value = readBack ? preferences.ReadBackDefaultMode() : preferences.LoadDefaultMode();
        _ = ResponseOutputModeResolver.Resolve(value ?? ResponseOutputMode.Hybrid, null, null);
        return value;
    }

    public void Observe()
    {
        lock (gate)
        {
            if (applying) { throw new InvalidOperationException("Response preference is committing."); }
            try
            {
                var value = Read();
                if (!held && saved == value) { return; }
                playback.InvalidateOutput();
                saved = value;
                desired = value ?? ResponseOutputMode.Hybrid;
                held = false;
                recovery = null;
                revision = checked(revision + 1);
                choices = [];
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                HoldUnavailable();
                throw;
            }
        }
    }

    public async Task RefreshAsync(RequestOrigin origin, Func<bool> eligible, CancellationToken cancellationToken)
    {
        var entered = false;
        var expectedRevision = 0L;
        try
        {
            await admission.RunAsync(origin, eligible, (request, authority) =>
            {
                lock (gate)
                {
                    if (applying) { throw new InvalidOperationException("Response preference is committing."); }
                    applying = true;
                    entered = true;
                    var value = Read();
                    held = true;
                    playback.InvalidateOutput();
                    saved = value;
                    desired = value ?? ResponseOutputMode.Hybrid;
                    recovery = null;
                    revision = checked(revision + 1);
                    expectedRevision = revision;
                    choices = Array.AsReadOnly(Enum.GetValues<ResponseOutputMode>()
                        .Select(mode => new ResponseModeChoice(mode, revision, owner, authority, request.Origin, eligible)).ToArray());
                    return true;
                }
            }, cancellationToken).ConfigureAwait(false);
            lock (gate)
            {
                if (revision != expectedRevision || !eligible()) { throw new InvalidOperationException("Response discovery admission changed before its receipt completed."); }
                held = false;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch
        {
            if (entered) { HoldUnavailable(); }
            throw;
        }
        finally { if (entered) { lock (gate) { applying = false; } } }
    }

    public async Task<bool> SelectAsync(ResponseModeChoice choice, long callRevision, RequestOrigin origin,
        SecurityAuditInitiator initiator, CallCommunicationPolicy policy, Func<bool> eligible, CancellationToken cancellationToken)
    {
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand))
        {
            throw new InvalidOperationException("Response-mode control requires original user initiation.");
        }
        if (initiator == SecurityAuditInitiator.VoiceCommand && origin != RequestOrigin.ActivatedVoice)
        {
            throw new InvalidOperationException("Original activated voice provenance is required.");
        }
        var entered = false;
        var expectedRevision = 0L;
        try
        {
            var accepted = await admission.RunAsync(origin, eligible, (request, authority) =>
            {
                lock (gate)
                {
                    if (held || applying || choice.Owner != owner || choice.Session != authority || choice.Revision != revision
                        || choice.Origin != request.Origin || !choice.RemainsAdmitted()
                        || !choices.Any(item => ReferenceEquals(item, choice)) || Read() != saved)
                    {
                        throw new InvalidOperationException("Response choice, saved state or original session/generation changed. Inspect and choose again.");
                    }
                    applying = true;
                    entered = true;
                    var nextRevision = checked(revision + 1);
                    var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
                        "configuration.response-output", SecurityAuditOutcome.Requested,
                        request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : initiator,
                        "preferences.device-local");
                    auditLog.Write(audit);
                    var denied = policy.CommitVoiceSetting(request.Origin, callRevision,
                        () => ReferenceEquals(HostActivity.RequireCurrent().Request, request) && eligible()
                            && !cancellationToken.IsCancellationRequested, () =>
                        {
                            held = true;
                            playback.InvalidateOutput();
                            try
                            {
                                preferences.BeginDefaultModeWrite();
                                preferences.SaveDefaultMode(choice.Mode);
                                if (Read(readBack: true) != choice.Mode) { throw new InvalidDataException("Saved response mode readback does not match the exact selection."); }
                            }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentOutOfRangeException)
                            {
                                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed,
                                    exception is IOException ? "io-error" : exception is UnauthorizedAccessException ? "access-denied" : "invalid-data"));
                                throw;
                            }
                            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                            saved = desired = choice.Mode;
                            revision = nextRevision;
                            expectedRevision = nextRevision;
                            choices = [];
                        });
                    if (denied is { } reason)
                    {
                        auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Denied, reason.ToString().ToLowerInvariant()));
                        return false;
                    }
                    return true;
                }
            }, cancellationToken).ConfigureAwait(false);
            if (accepted)
            {
                lock (gate)
                {
                    if (revision != expectedRevision || !eligible()) { throw new InvalidOperationException("Response apply admission changed before its receipt completed."); }
                    if (Read(readBack: true) != choice.Mode) { throw new InvalidDataException("Saved response mode changed before its completed receipt."); }
                    preferences.ConfirmDefaultModeWrite();
                    try
                    {
                        if (Read() != choice.Mode) { throw new InvalidDataException("Confirmed response mode readback does not match the exact selection."); }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentOutOfRangeException)
                    {
                        preferences.BeginDefaultModeWrite();
                        throw;
                    }
                    held = false;
                    recovery = null;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
            return accepted;
        }
        catch
        {
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
            recovery = "Response preference or admission/evidence is unconfirmed. Inspect saved state before a fresh request; full visual remains, no automatic retry.";
            choices = [];
            revision = checked(revision + 1);
            try { playback.InvalidateOutput(); }
            finally { Changed?.Invoke(this, EventArgs.Empty); }
        }
    }
}
