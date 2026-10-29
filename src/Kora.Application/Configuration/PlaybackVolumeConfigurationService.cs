using Kora.Application.Communication;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed partial class PlaybackVolumeConfigurationService(
    IPlaybackVolumePreferences preferences, ISpeechPlaybackService playback,
    AudioControlAdmission admission, ISecurityAuditLog auditLog)
{
    private readonly Lock gate = new();
    private PlaybackVolumeState state = new(null, null, "unavailable", 0, "Refresh the volume preference in the owning unlocked host.");
    private PlaybackVolumeProposal? proposal;
    private (WorkSessionAuthorization Session, RequestOrigin Origin, Func<bool> Eligible)? input;
    private bool applying;
    private bool publishing;

    public event EventHandler? Changed;
    public PlaybackVolumeState Get() { lock (gate) { return state; } }

    public void Observe()
    {
        lock (gate)
        {
            if (applying || publishing) { throw new InvalidOperationException("Volume is committing or notifying."); }
            var saved = preferences.Load();
            var desired = saved ?? PlaybackVolume.Default;
            var effective = playback is IPlaybackVolumeControl ? desired : (PlaybackVolume?)null;
            if (state.Desired == desired && state.Effective == effective
                && string.Equals(state.Source, saved is null ? "default" : "saved", StringComparison.Ordinal)) { return; }
            proposal = null;
            playback.InvalidateOutput();
            if (playback is IPlaybackVolumeControl control) { control.SetPlaybackVolume(effective); }
            state = new(desired, effective, saved is null ? "default" : "saved", checked(state.Revision + 1),
                effective is null ? "This playback adapter has no qualified Kora-only gain capability. Full output remains visual."
                    : desired.AllowsSpeech ? null : "Kora volume is zero: no synthesis or automatic playback; full output remains visual.");
            Publish();
        }
    }

    public async Task RefreshAsync(RequestOrigin originalOrigin, Func<bool> remainsEligible, CancellationToken token,
        long? expectedRevision = null)
    {
        try
        {
            var observed = await admission.RunAsync(originalOrigin, remainsEligible, (request, authority) =>
            {
                lock (gate)
                {
                    if (applying || publishing) { throw new InvalidOperationException("Volume is committing or notifying."); }
                    if (expectedRevision is { } revision && state.Revision != revision)
                    { throw new InvalidOperationException("The reviewed volume revision changed before fresh admission."); }
                    var saved = preferences.Load();
                    if (input is not { } current || current.Session != authority || current.Origin != request.Origin || !current.Eligible())
                    {
                        proposal = null;
                        state = state with { Revision = checked(state.Revision + 1) };
                    }
                    input = (authority, request.Origin, remainsEligible);
                    return (Saved: saved, Revision: state.Revision);
                }
            }, token).ConfigureAwait(false);
            lock (gate)
            {
                if (!remainsEligible() || state.Revision != observed.Revision || preferences.Load() != observed.Saved)
                { throw new InvalidOperationException("Volume discovery admission or saved state changed before its receipt."); }
                Observe();
            }
        }
        catch { HoldUnavailable(); throw; }
    }

    public PlaybackVolumeProposal Propose(string? value, long expectedRevision, long callRevision)
    {
        var parsed = value is null ? (PlaybackVolume?)null : PlaybackVolume.Parse(value);
        lock (gate)
        {
            var current = RequireProposalAdmission(expectedRevision);
            proposal = new(parsed, expectedRevision, callRevision, current.Session, current.Origin, current.Eligible);
            return proposal;
        }
    }

    /// <summary>Proposes one step from admitted desired state; returns <c>null</c> only at an unchanged boundary.</summary>
    public PlaybackVolumeProposal? ProposeAdjustment(AudioPreferenceAdjustment adjustment, long expectedRevision, long callRevision)
    {
        lock (gate)
        {
            var current = RequireProposalAdmission(expectedRevision);
            proposal = null;
            var desired = state.Desired!.Value;
            if ((preferences.Load() ?? PlaybackVolume.Default) != desired)
            {
                throw new InvalidOperationException("Saved volume changed; refresh before proposing a relative step.");
            }
            var adjusted = desired.Adjust(adjustment);
            if (adjusted == desired) { return null; }
            proposal = new(adjusted, expectedRevision, callRevision, current.Session, current.Origin, current.Eligible);
            return proposal;
        }
    }

    private (WorkSessionAuthorization Session, RequestOrigin Origin, Func<bool> Eligible) RequireProposalAdmission(long expectedRevision)
    {
        if (applying || publishing || expectedRevision != state.Revision || input is not { } current || !current.Eligible()
            || !state.Available)
        {
            throw new InvalidOperationException("Refresh volume under the current original input and choose again.");
        }
        return current;
    }

    public async Task<bool> ApplyAsync(PlaybackVolumeProposal candidate, RequestOrigin originalOrigin,
        SecurityAuditInitiator initiator, CallCommunicationPolicy policy, Func<bool> remainsEligible, CancellationToken token)
    {
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || initiator == SecurityAuditInitiator.VoiceCommand && originalOrigin != RequestOrigin.ActivatedVoice)
        {
            throw new InvalidOperationException("Original activated input provenance is required.");
        }
        PlaybackVolume? previous = null;
        try
        {
            var accepted = await admission.RunAsync(originalOrigin, remainsEligible, (request, authority) =>
            {
                lock (gate)
                {
                    if (applying || publishing || !ReferenceEquals(proposal, candidate) || candidate.Session != authority
                        || candidate.Origin != request.Origin || candidate.Revision != state.Revision
                        || !candidate.Eligible() || !state.Available
                        || preferences.Load() != (string.Equals(state.Source, "saved", StringComparison.Ordinal) ? state.Desired : null))
                    {
                        throw new InvalidOperationException("Volume proposal, saved state or original admission changed.");
                    }
                    applying = true;
                    previous = preferences.Load();
                    var mutationOrigin = originalOrigin == RequestOrigin.ActivatedVoice ? originalOrigin : request.Origin;
                    var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                        "configuration.playback-volume", SecurityAuditOutcome.Requested,
                        mutationOrigin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : initiator,
                        "preferences.device-local");
                    UndoChanged?.Invoke(null);
                    auditLog.Write(audit);
                    var denied = policy.CommitVoiceSetting(mutationOrigin, candidate.CallRevision,
                        () => ReferenceEquals(HostActivity.RequireCurrent().Request, request)
                            && remainsEligible() && candidate.Eligible() && !token.IsCancellationRequested
                            && ReferenceEquals(proposal, candidate) && candidate.Revision == state.Revision && state.Available, () =>
                        {
                            try
                            {
                                if (preferences.Load() != previous)
                                {
                                    throw new InvalidDataException("Saved volume changed during the requested audit; refresh before a new proposal.");
                                }
                                playback.InvalidateOutput();
                                if (playback is IPlaybackVolumeControl control) { control.SetPlaybackVolume(null); }
                                preferences.BeginWrite();
                                if (candidate.Value is { } volume) { preferences.Save(volume); }
                                else { preferences.Reset(); }
                                if (preferences.ReadBack() != candidate.Value) { throw new InvalidDataException("Volume readback did not match the exact preference."); }
                            }
                            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                            {
                                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed,
                                    exception is InvalidDataException ? "invalid-data" : exception is IOException ? "io-error" : "access-denied"));
                                throw;
                            }
                            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
                            state = state with { Effective = null, Revision = checked(state.Revision + 1) };
                            proposal = null;
                        });
                    if (denied is { } reason)
                    {
                        auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Denied, reason.ToString().ToLowerInvariant()));
                        return false;
                    }
                    return true;
                }
            }, token).ConfigureAwait(false);
            lock (gate)
            {
                if (accepted)
                {
                    if (!remainsEligible()) { throw new InvalidOperationException("Volume admission changed before its apply receipt."); }
                    if (preferences.ReadBack() != candidate.Value) { throw new InvalidDataException("Saved volume changed before its completed apply receipt."); }
                    preferences.ConfirmWrite();
                    try
                    {
                        if (preferences.Load() != candidate.Value) { throw new InvalidDataException("Confirmed volume readback did not match the exact preference."); }
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
                    {
                        preferences.BeginWrite();
                        throw;
                    }
                    applying = false;
                    Observe();
                    RecordVolumeUndo(previous, candidate.Value);
                }
            }
            return accepted;
        }
        catch { HoldUnavailable(); throw; }
        finally { lock (gate) { applying = false; } }
    }

    public void HoldUnavailable()
    {
        lock (gate)
        {
            proposal = null;
            state = state with { Effective = null, Source = "unavailable", Revision = checked(state.Revision + 1),
                Recovery = "Volume preference or admission/evidence is unconfirmed. Inspect saved state and explicitly refresh; no automatic retry." };
            try
            {
                playback.InvalidateOutput();
                if (playback is IPlaybackVolumeControl control) { control.SetPlaybackVolume(null); }
            }
            finally { Publish(); }
        }
    }

    private void Publish()
    {
        publishing = true;
        try { Changed?.Invoke(this, EventArgs.Empty); }
        finally { publishing = false; }
    }
}
