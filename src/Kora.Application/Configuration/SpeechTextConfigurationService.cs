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
    private IReadOnlyList<SpeechTextChoice> captionChoices = [];
    private SpeechTextMode? saved;
    private SpeechCaptionOptions? savedOptions;
    private long revision;
    private bool applying;
    private bool held = true;
    public event EventHandler? Changed;
    public IReadOnlyList<SpeechTextChoice> Choices { get { lock (gate) { return choices; } } }
    public IReadOnlyList<SpeechTextChoice> CaptionChoices { get { lock (gate) { return captionChoices; } } }

    public SpeechTextState Get(string outcome = "observed")
    {
        lock (gate)
        {
            return new(outcome, revision, saved, held ? null : saved ?? SpeechTextMode.Off,
                held ? "unavailable" : saved is null ? "default" : "saved", !held,
                held ? "Speech-text preference or admission/evidence is unconfirmed. Captions remain off; inspect saved state and audit receipts before explicit repair." : null,
                savedOptions, held ? null : savedOptions ?? SpeechCaptionOptions.Default);
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
                var options = preferences.LoadOptions();
                if (!held && saved == value && savedOptions == options) { return; }
                saved = value;
                savedOptions = options;
                held = false;
                revision = checked(revision + 1);
                choices = [];
                captionChoices = [];
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
                    savedOptions = preferences.LoadOptions();
                    revision = checked(revision + 1);
                    expectedRevision = revision;
                    choices = Array.AsReadOnly(Enum.GetValues<SpeechTextMode>().Select(mode =>
                        new SpeechTextChoice(mode, revision, owner, authority, request.Origin, eligible)).ToArray());
                    captionChoices = Array.AsReadOnly(
                        Enum.GetValues<SpeechCaptionPlacement>().Select(placement => (SpeechCaptionValue)new SpeechCaptionValue.Placement(placement))
                            .Concat(Enumerable.Range(SpeechCaptionOptions.MinimumDelaySeconds,
                                SpeechCaptionOptions.MaximumDelaySeconds - SpeechCaptionOptions.MinimumDelaySeconds + 1)
                                .Select(seconds => new SpeechCaptionValue.Delay(seconds)))
                            .Select(value => new SpeechTextChoice(null, revision, owner, authority, request.Origin, eligible, value)).ToArray());
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
        SpeechCaptionOptions? expectedOptions = null;
        SpeechTextMode? expectedMode = null;
        try
        {
            var accepted = await admission.RunAsync(origin, eligible, (request, authority) =>
            {
                lock (gate)
                {
                    if (held || applying || choice.Owner != owner || choice.Session != authority || choice.Revision != revision
                        || choice.Origin != request.Origin || !choice.Eligible()
                        || !choices.Concat(captionChoices).Any(item => ReferenceEquals(item, choice)))
                    {
                        throw new InvalidOperationException("Speech-text choice, session/generation or saved state changed. Inspect and choose again.");
                    }
                    if (Read() != saved || preferences.LoadOptions() != savedOptions)
                    {
                        HoldUnavailable();
                        throw new InvalidOperationException("Saved speech-text settings changed. Inspect and choose again.");
                    }
                    applying = true;
                    entered = true;
                    expectedMode = choice.CaptionValue is null ? choice.Mode : saved;
                    expectedOptions = choice.CaptionValue is { } value
                        ? (savedOptions ?? SpeechCaptionOptions.Default).With(value) : savedOptions;
                    var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
                        choice.CaptionValue?.Option switch
                        {
                            SpeechCaptionOption.Placement => "configuration.speech-caption-placement",
                            SpeechCaptionOption.DismissalDelay => "configuration.speech-caption-dismissal-delay",
                            _ => "configuration.speech-text",
                        }, SecurityAuditOutcome.Requested,
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
                                if (choice.CaptionValue is null) { preferences.Save(expectedMode!.Value); }
                                else { preferences.SaveOptions(expectedOptions!); }
                                RequireReadback();
                            }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentOutOfRangeException)
                            {
                                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed,
                                    exception is InvalidDataException ? "invalid-data" : exception is IOException ? "io-error" : "access-denied"));
                                throw;
                            }
                            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                            saved = expectedMode;
                            savedOptions = expectedOptions;
                            expectedRevision = revision;
                            choices = [];
                            captionChoices = [];
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
                    RequireReadback();
                    preferences.ConfirmWrite();
                    try
                    {
                        if (Read() != expectedMode || preferences.LoadOptions() != expectedOptions)
                        {
                            throw new InvalidDataException("Confirmed speech-text readback does not match the selection.");
                        }
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

        void RequireReadback()
        {
            if (Read(readBack: true) != expectedMode || preferences.ReadBackOptions() != expectedOptions)
            {
                throw new InvalidDataException("Speech-text readback does not match the exact selection.");
            }
        }
    }

    public void HoldUnavailable()
    {
        lock (gate)
        {
            held = true;
            choices = [];
            captionChoices = [];
            revision = checked(revision + 1);
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
