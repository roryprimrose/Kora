using Kora.Application.Communication;
using Kora.Application.Voice;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class WindowsSpeechRateConfigurationService(
    IWindowsSpeechRatePreferences preferences, ISpeechPlaybackService playback,
    SpeechConfigurationService speech, AudioControlAdmission admission,
    ISecurityAuditLog auditLog, ILogger<WindowsSpeechRateConfigurationService> logger)
{
    private readonly Lock gate = new();
    private WindowsSpeechRateState state = new(null, null, null, SpeechRateSupport.Unsupported, 0,
        "unavailable", 0, "Refresh the Windows rate preference in the owning unlocked host.");
    private WindowsSpeechRateProposal? proposal;
    private WorkSessionAuthorization? session;
    private RequestOrigin origin;
    private Func<bool>? eligible;
    private bool applying;
    private bool publishing;

    public event EventHandler? Changed;
    public WindowsSpeechRateState Get() { lock (gate) { return state; } }

    private bool ProviderMatches()
    {
        var current = speech.Get();
        return current.IsAvailable && speech.IsCurrentSelection(state.ProviderRevision)
            && string.Equals(current.Selection!.ProviderId, state.ProviderId, StringComparison.Ordinal)
            && current.Providers.Count(provider => provider.IsInstalled
                && string.Equals(provider.Id, SpeechProviderIds.Windows, StringComparison.Ordinal)
                && provider.RateSupport == SpeechRateSupport.WindowsNative) == 1;
    }

    public void Observe()
    {
        lock (gate)
        {
            if (applying || publishing) { throw new InvalidOperationException("Rate is committing or notifying."); }
            try
            {
                var saved = preferences.Load();
                var desired = saved ?? WindowsSpeechRate.Default;
                var current = speech.Get();
                var providerId = current.Selection?.ProviderId;
                var matches = current.Providers.Where(provider => string.Equals(provider.Id, providerId, StringComparison.Ordinal)).ToArray();
                var support = current.IsAvailable && matches.Length == 1 && matches[0].IsInstalled
                    && string.Equals(providerId, SpeechProviderIds.Windows, StringComparison.Ordinal)
                    && matches[0].RateSupport == SpeechRateSupport.WindowsNative
                    ? SpeechRateSupport.WindowsNative : SpeechRateSupport.Unsupported;
                var effective = support == SpeechRateSupport.WindowsNative && playback is IWindowsSpeechRateControl
                    ? desired : (WindowsSpeechRate?)null;
                var source = saved is null ? "default" : "saved";
                if (state.Desired == desired && state.Effective == effective && string.Equals(state.Source, source, StringComparison.Ordinal)
                    && string.Equals(state.ProviderId, providerId, StringComparison.Ordinal)
                    && state.Support == support && state.ProviderRevision == current.Revision) { return; }
                proposal = null;
                playback.InvalidateOutput();
                if (playback is IWindowsSpeechRateControl control) { control.SetWindowsSpeechRate(effective); }
                state = new(desired, effective, providerId, support, current.Revision, source, checked(state.Revision + 1),
                    effective is null ? "Windows-native rate is unsupported or unavailable for the current provider/adapter. Kokoro synthesis is unchanged; no common speed scale is applied." : null);
                Publish();
            }
            catch (Exception exception)
            {
                PreferenceUnavailable(logger, exception);
                HoldUnavailable();
                throw;
            }
        }
    }

    public async Task RefreshAsync(RequestOrigin originalOrigin, Func<bool> remainsEligible, CancellationToken token)
    {
        try
        {
            var observed = await admission.RunAsync(originalOrigin, remainsEligible, (request, authority) =>
            {
                lock (gate)
                {
                    if (applying || publishing) { throw new InvalidOperationException("Rate is committing or notifying."); }
                    speech.Reload();
                    var saved = preferences.Load();
                    if (session != authority || origin != request.Origin || eligible is null || !eligible())
                    {
                        proposal = null;
                        state = state with { Revision = checked(state.Revision + 1) };
                    }
                    session = authority;
                    origin = request.Origin;
                    eligible = remainsEligible;
                    return saved;
                }
            }, token).ConfigureAwait(false);
            lock (gate)
            {
                if (!remainsEligible() || preferences.Load() != observed) { throw new InvalidOperationException("Rate discovery admission or saved state changed before its receipt."); }
                Observe();
            }
        }
        catch (Exception exception) { PreferenceUnavailable(logger, exception); HoldUnavailable(); throw; }
    }

    public WindowsSpeechRateProposal Propose(string? value, long expectedRevision, long callRevision)
    {
        var parsed = value is null ? (WindowsSpeechRate?)null : WindowsSpeechRate.Parse(value);
        lock (gate)
        {
            if (applying || publishing || expectedRevision != state.Revision || session is null || eligible is null || !eligible()
                || !state.Available || !ProviderMatches())
            {
                throw new InvalidOperationException("Refresh rate under the current original input and supported Windows provider, then choose again.");
            }
            proposal = new(parsed, expectedRevision, callRevision, session, origin, eligible);
            return proposal;
        }
    }

    public async Task<bool> ApplyAsync(WindowsSpeechRateProposal candidate, RequestOrigin originalOrigin,
        SecurityAuditInitiator initiator, CallCommunicationPolicy policy, Func<bool> remainsEligible, CancellationToken token)
    {
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand)
            || initiator == SecurityAuditInitiator.VoiceCommand && originalOrigin != RequestOrigin.ActivatedVoice)
        {
            throw new InvalidOperationException("Original activated input provenance is required.");
        }
        try
        {
            var accepted = await admission.RunAsync(originalOrigin, remainsEligible, (request, authority) =>
            {
                lock (gate)
                {
                    if (applying || publishing || !ReferenceEquals(proposal, candidate) || candidate.Session != authority
                        || candidate.Origin != request.Origin || candidate.Revision != state.Revision
                        || !candidate.Eligible() || !state.Available || !ProviderMatches()
                        || (preferences.Load() ?? WindowsSpeechRate.Default) != state.Desired)
                    {
                        throw new InvalidOperationException("Rate proposal, provider, saved state or original admission changed.");
                    }
                    applying = true;
                    var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                        "configuration.windows-speech-rate", SecurityAuditOutcome.Requested,
                        request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : initiator,
                        "preferences.device-local");
                    auditLog.Write(audit);
                    var denied = policy.CommitVoiceSetting(request.Origin, candidate.CallRevision,
                        () => ReferenceEquals(HostActivity.RequireCurrent().Request, request)
                            && remainsEligible() && candidate.Eligible() && !token.IsCancellationRequested
                            && ReferenceEquals(proposal, candidate) && candidate.Revision == state.Revision
                            && state.Available && ProviderMatches(), () =>
                        {
                            try
                            {
                                if ((preferences.Load() ?? WindowsSpeechRate.Default) != state.Desired)
                                {
                                    throw new InvalidDataException("Saved rate changed during the requested audit; refresh before a new proposal.");
                                }
                                playback.InvalidateOutput();
                                if (playback is IWindowsSpeechRateControl control) { control.SetWindowsSpeechRate(null); }
                                preferences.BeginWrite();
                                if (candidate.Value is { } rate) { preferences.Save(rate); }
                                else { preferences.Reset(); }
                                if (preferences.ReadBack() != candidate.Value) { throw new InvalidDataException("Rate readback did not match the exact preference."); }
                            }
                            catch (Exception exception)
                            {
                                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed,
                                    exception is InvalidDataException ? "invalid-data" : exception is IOException ? "io-error"
                                    : exception is UnauthorizedAccessException ? "access-denied" : "operation-failed"));
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
                    if (!remainsEligible() || !ProviderMatches()) { throw new InvalidOperationException("Rate admission or provider changed before its apply receipt."); }
                    if (preferences.ReadBack() != candidate.Value) { throw new InvalidDataException("Saved rate changed before its completed apply receipt."); }
                    preferences.ConfirmWrite();
                    try
                    {
                        if (preferences.Load() != candidate.Value) { throw new InvalidDataException("Confirmed rate readback did not match the exact preference."); }
                        applying = false;
                        Observe();
                    }
                    catch
                    {
                        preferences.BeginWrite();
                        throw;
                    }
                }
            }
            return accepted;
        }
        catch (Exception exception) { PreferenceUnavailable(logger, exception); HoldUnavailable(); throw; }
        finally { lock (gate) { applying = false; } }
    }

    public void HoldUnavailable()
    {
        lock (gate)
        {
            proposal = null;
            state = state with { Effective = null, Source = "unavailable", Revision = checked(state.Revision + 1),
                Recovery = "Windows rate preference or admission/evidence is unconfirmed. Inspect saved state and explicitly refresh; no automatic retry. Kokoro remains unchanged." };
            try
            {
                playback.InvalidateOutput();
                if (playback is IWindowsSpeechRateControl control) { control.SetWindowsSpeechRate(null); }
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
