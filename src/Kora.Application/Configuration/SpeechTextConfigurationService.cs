using Kora.Application.Communication;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed class SpeechTextConfigurationService(
    ISpeechTextPreferences preferences, AudioControlAdmission admission, ISecurityAuditLog auditLog)
{
    private readonly Lock gate = new();
    private readonly Guid owner = Guid.NewGuid();
    private IReadOnlyList<SpeechTextChoice> choices = [];
    private SpeechTextMode? saved;
    private long revision;
    private bool applying;
    private bool held = true;
    public event EventHandler? Changed;
    public IReadOnlyList<SpeechTextChoice> Choices { get { lock (gate) { return choices; } } }

    public SpeechTextState Get(string outcome = "observed")
    {
        lock (gate)
        {
            return new(outcome, revision, saved, held ? null : saved ?? SpeechTextMode.Off,
                held ? "unavailable" : saved is null ? "default" : "saved", !held,
                held ? "Speech-text preference or admission/evidence is unconfirmed. Captions remain off; inspect saved state and audit receipts before explicit repair." : null);
        }
    }

    private SpeechTextMode? Read(bool readBack = false)
    {
        var value = readBack ? preferences.ReadBack() : preferences.Load();
        if (value is { } mode && !Enum.IsDefined(mode)) { throw new InvalidDataException("Invalid saved speech-text mode."); }
        return value;
    }

    public void Observe()
    {
        lock (gate)
        {
            if (applying) { throw new InvalidOperationException("Speech-text preference is committing."); }
            try
            {
                var value = Read();
                if (!held && saved == value) { return; }
                saved = value;
                held = false;
                revision = checked(revision + 1);
                choices = [];
                Changed?.Invoke(this, EventArgs.Empty);
            }
            catch { HoldUnavailable(); throw; }
        }
    }

    public async Task RefreshAsync(RequestOrigin origin, Func<bool> eligible, CancellationToken cancellationToken)
    {
        var entered = false;
        var expectedRevision = 0L;
        try
        {
            lock (gate)
            {
                if (applying) { throw new InvalidOperationException("Speech-text preference is committing."); }
            }
            await admission.RunAsync(origin, eligible, (request, authority) =>
            {
                lock (gate)
                {
                    applying = true;
                    entered = true;
                    held = true;
                    saved = Read();
                    revision = checked(revision + 1);
                    expectedRevision = revision;
                    choices = Array.AsReadOnly(Enum.GetValues<SpeechTextMode>().Select(mode =>
                        new SpeechTextChoice(mode, revision, owner, authority, request.Origin, eligible)).ToArray());
                    Changed?.Invoke(this, EventArgs.Empty);
                    return true;
                }
            }, cancellationToken).ConfigureAwait(false);
            lock (gate)
            {
                if (revision != expectedRevision || !eligible()) { throw new InvalidOperationException("Speech-text discovery admission changed before its receipt completed."); }
                held = false;
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }
        catch { if (entered) { HoldUnavailable(); } throw; }
        finally { if (entered) { lock (gate) { applying = false; } } }
    }

    public async Task<bool> SelectAsync(SpeechTextChoice choice, long callRevision, RequestOrigin origin,
        SecurityAuditInitiator initiator, CallCommunicationPolicy policy, Func<bool> eligible, CancellationToken cancellationToken)
    {
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || initiator == SecurityAuditInitiator.VoiceCommand && origin != RequestOrigin.ActivatedVoice)
        {
            throw new InvalidOperationException("Speech-text control requires original admitted user initiation.");
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
                        || choice.Origin != request.Origin || !choice.Eligible() || !choices.Any(item => ReferenceEquals(item, choice))
                        || Read() != saved)
                    {
                        throw new InvalidOperationException("Speech-text choice, session/generation or saved state changed. Inspect and choose again.");
                    }
                    applying = true;
                    entered = true;
                    var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
                        "configuration.speech-text", SecurityAuditOutcome.Requested,
                        request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : initiator,
                        "preferences.device-local");
                    auditLog.Write(audit);
                    var denied = policy.CommitVoiceSetting(request.Origin, callRevision,
                        () => ReferenceEquals(HostActivity.RequireCurrent().Request, request) && eligible()
                            && !cancellationToken.IsCancellationRequested, () =>
                        {
                            held = true;
                            revision = checked(revision + 1);
                            Changed?.Invoke(this, EventArgs.Empty);
                            try
                            {
                                preferences.BeginWrite();
                                preferences.Save(choice.Mode);
                                if (Read(readBack: true) != choice.Mode) { throw new InvalidDataException("Speech-text readback does not match the exact selection."); }
                            }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
                            {
                                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed,
                                    exception is InvalidDataException ? "invalid-data" : exception is IOException ? "io-error" : "access-denied"));
                                throw;
                            }
                            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                            saved = choice.Mode;
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
            }, cancellationToken).ConfigureAwait(false);
            if (accepted)
            {
                lock (gate)
                {
                    if (revision != expectedRevision || !eligible()) { throw new InvalidOperationException("Speech-text apply admission changed before its receipt completed."); }
                    if (Read(readBack: true) != choice.Mode) { throw new InvalidDataException("Speech-text changed before its completed receipt."); }
                    preferences.ConfirmWrite();
                    try
                    {
                        if (Read() != choice.Mode) { throw new InvalidDataException("Confirmed speech-text readback does not match the selection."); }
                    }
                    catch { preferences.BeginWrite(); throw; }
                    held = false;
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
            return accepted;
        }
        catch { if (entered) { HoldUnavailable(); } throw; }
        finally { if (entered) { lock (gate) { applying = false; } } }
    }

    public void HoldUnavailable()
    {
        lock (gate)
        {
            held = true;
            choices = [];
            revision = checked(revision + 1);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
