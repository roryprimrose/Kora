using Kora.Application.Communication;
using Kora.Core.Auditing;
using Kora.Core.Configuration;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Voice;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Configuration;

public sealed partial class SpeechConfigurationService(
    ITextToSpeechPreferences preferences, ISpeechCatalog catalog,
    ISecurityAuditLog auditLog, ILogger<SpeechConfigurationService> logger)
{
    private readonly Lock gate = new();
    private readonly Guid owner = Guid.NewGuid();
    private SpeechConfigurationState? state;
    private string? heldRecovery;
    private bool publishing;

    public event EventHandler? Changed;

    public SpeechConfigurationState Get()
    {
        lock (gate)
        {
            if (state is null) { Reload(); }
            return state!;
        }
    }

    public void Reload() => Reload(preserveHold: false);

    internal bool IsCurrentSelection(long expectedRevision)
    {
        lock (gate)
        {
            var current = Get();
            if (current.Revision != expectedRevision) { return false; }
            var saved = preferences.LoadSelection();
            if ((saved ?? SpeechSelection.Default) != current.Selection || (saved is not null) != current.IsSaved) { return false; }
            var discovered = Discover(current.Selection, current.IsSaved, current.Recovery, current.Revision);
            return current.EffectiveVoice == discovered.EffectiveVoice
                && current.Providers.SequenceEqual(discovered.Providers) && current.Voices.SequenceEqual(discovered.Voices);
        }
    }

    private void Reload(bool preserveHold)
    {
        lock (gate)
        {
            EnsureNotPublishing();
            if (!preserveHold) { heldRecovery = null; }
            SpeechSelection? selection;
            bool saved;
            string? error = null;
            try
            {
                var persisted = preferences.LoadSelection();
                selection = persisted ?? SpeechSelection.Default;
                saved = persisted is not null;
                selection.Validate();
            }
            catch (Exception exception) when (exception is InvalidDataException or ArgumentOutOfRangeException)
            {
                InvalidSavedSelection(logger, exception);
                selection = null;
                saved = true;
                error = "The saved speech selection is invalid. Reset speech.provider or choose an installed provider.";
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                SelectionReadFailed(logger, exception);
                selection = null;
                saved = true;
                error = "The saved speech selection could not be read. Repair local preference access and refresh, or explicitly choose/reset an installed provider.";
            }
            SpeechConfigurationState next;
            try
            {
                next = Discover(selection, saved, error ?? heldRecovery, state?.Revision ?? 0);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                CatalogueReadFailed(logger, exception);
                next = new(selection, null, [], [], state?.Revision ?? 0, saved,
                    "Installed speech discovery failed. Repair the local assets/access and refresh. Visual responses remain available; no provider is substituted.");
            }
            try
            {
                var limits = preferences.LoadSummaryLimits();
                limits?.Validate();
                next = next with { SummaryLimits = limits ?? SpokenSummaryLimits.Default, AreSummaryLimitsSaved = limits is not null };
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentOutOfRangeException)
            {
                SummaryLimitsReadFailed(logger, exception);
                next = next with { SummaryLimits = null, AreSummaryLimitsSaved = true,
                    SummaryLimitsRecovery = "Spoken summary limits could not be read or are invalid. Ordinary speech is unavailable; repair the local file and refresh. Unknown companion limits cannot be defaulted by a per-option reset. Full results remain visual." };
            }
            if (state is null || !Equivalent(state, next))
            {
                state = next with { Revision = checked(next.Revision + 1) };
                Publish();
            }
        }
    }

    public SpeechProposal Propose(SpeechOption option, string? value, long expectedRevision,
        long callRevision, RequestOrigin origin, SecurityAuditInitiator initiator)
    {
        ValidateInitiator(initiator);
        _ = SpeechOptionRegistry.Get(option);
        lock (gate)
        {
            var current = Get();
            SpeechSelection selection;
            SpokenSummaryLimits? limits = null;
            if (SpeechOptionRegistry.Get(option).IsSummaryLimit)
            {
                if (current.SummaryLimits is null)
                {
                    throw new InvalidOperationException("Repair the saved summary limits file and refresh; unknown companion limits cannot be defaulted by a per-option reset.");
                }
                limits = current.SummaryLimits.With(option, value);
                selection = current.Selection ?? SpeechSelection.Default;
            }
            else if (option == SpeechOption.Provider)
            {
                selection = new(value ?? SpeechSelection.Default.ProviderId, null);
            }
            else if (value is null)
            {
                selection = new(current.Selection?.ProviderId
                    ?? throw new InvalidOperationException("Recover the speech provider first."), null);
            }
            else
            {
                var matching = current.Voices.Where(voice =>
                    string.Equals(voice.ConfigurationId, value, StringComparison.Ordinal)
                    || string.Equals(voice.Id, value, StringComparison.Ordinal)).ToArray();
                if (matching.Length != 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(value),
                        "Choose one exact installed provider / voice ID. Missing or ambiguous voice IDs cannot be selected.");
                }
                selection = new(matching[0].ProviderId, matching[0].Id);
            }
            selection.Validate();
            // A later UI surface cannot relabel the original voice request.
            origin = HostActivity.Current?.Request.Origin ?? origin;
            if (initiator == SecurityAuditInitiator.VoiceCommand) { origin = RequestOrigin.ActivatedVoice; }
            return new(owner, option, selection, expectedRevision, callRevision, origin, initiator, limits);
        }
    }

    public SpeechProposal ProposeReset(SpeechOption option, long expectedRevision,
        long callRevision, RequestOrigin origin, SecurityAuditInitiator initiator) =>
        Propose(option, null, expectedRevision, callRevision, origin, initiator);

    internal void HoldUnavailable(string recovery)
    {
        lock (gate)
        {
            EnsureNotPublishing();
            var current = state ?? new SpeechConfigurationState(null, null, [], [], 0, false, recovery);
            heldRecovery = recovery;
            state = current with { EffectiveVoice = null, Recovery = recovery, Revision = checked(current.Revision + 1) };
            Publish();
        }
    }

    internal SpeechApplyResult Apply(SpeechProposal proposal, CallCommunicationPolicy policy,
        Func<bool> hostEligible, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        lock (gate)
        {
            EnsureNotPublishing();
            cancellationToken.ThrowIfCancellationRequested();
            if (proposal.Owner != owner)
            {
                throw new ArgumentException("The speech proposal belongs to another host registry.", nameof(proposal));
            }
            var current = Get();
            var descriptor = SpeechOptionRegistry.Get(proposal.Option);
            var origin = proposal.Origin == RequestOrigin.ActivatedVoice ? proposal.Origin
                : HostActivity.Current?.Request.Origin ?? proposal.Origin;
            using var activity = HostActivity.Current is not null
                ? HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Storage)
                : HostActivity.BeginRoot(HostRequest.Create(origin),
                    HostActivityLayer.Application, HostOperation.Storage);
            var audit = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ConfigurationWrite,
                descriptor.AuditAction, SecurityAuditOutcome.Requested,
                origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : proposal.Initiator,
                "preferences.device-local");
            auditLog.Write(audit);
            SpeechApplyResult Reject(string reason, string recovery)
            {
                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Denied, reason));
                activity.Complete(HostOperationOutcome.Failed);
                MutationRejected(logger, descriptor.Id, reason);
                return new(false, Get(), recovery);
            }
            var candidate = current;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                // Re-discover after request evidence and before the serialized policy/write boundary.
                Reload(preserveHold: true);
                current = Get();
                if (current.Revision != proposal.Revision)
                {
                    return Reject("stale", "Speech configuration changed. Inspect it and submit a new choice.");
                }
                var denied = policy.CommitVoiceSetting(origin, proposal.CallRevision,
                    hostEligible, () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (descriptor.IsSummaryLimit)
                        {
                            var limits = proposal.SummaryLimits
                                ?? throw new InvalidOperationException("The proposal has no summary limits.");
                            candidate = current with { SummaryLimits = limits, AreSummaryLimitsSaved = true, SummaryLimitsRecovery = null };
                            if (current.SummaryLimits != limits || !current.AreSummaryLimitsSaved)
                            {
                                preferences.SaveSummaryLimits(limits);
                            }
                        }
                        else
                        {
                            candidate = Discover(proposal.Selection, true, null, current.Revision) with
                            {
                                SummaryLimits = current.SummaryLimits, AreSummaryLimitsSaved = current.AreSummaryLimitsSaved,
                                SummaryLimitsRecovery = current.SummaryLimitsRecovery,
                            };
                            if (!candidate.IsAvailable) { return; }
                            if (current.Selection != proposal.Selection || !current.IsSaved)
                            {
                                preferences.SaveSelection(proposal.Selection);
                            }
                        }
                    });
                if (denied is not null)
                {
                    return Reject(denied.Value.ToString().ToLowerInvariant(),
                        "The host or call policy changed, or rejects the original channel. Start a new eligible Settings request.");
                }
                if (!descriptor.IsSummaryLimit && !candidate.IsAvailable)
                {
                    Reload(preserveHold: true);
                    return Reject("assets-unavailable", candidate.Recovery!);
                }
            }
            catch (OperationCanceledException)
            {
                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Cancelled, "user-cancelled"));
                activity.Complete(HostOperationOutcome.Cancelled);
                throw;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Failed,
                    exception is IOException ? "io-error" : exception is UnauthorizedAccessException ? "access-denied" : "catalogue-unavailable"));
                activity.Complete(HostOperationOutcome.Failed);
                SaveFailed(logger, descriptor.Id, exception);
                return new(false, current, exception.Message);
            }
            auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded));
            activity.Complete(HostOperationOutcome.Completed);
            if (!Equivalent(current, candidate))
            {
                if (!descriptor.IsSummaryLimit) { heldRecovery = null; }
                state = candidate with { Revision = checked(current.Revision + 1) };
                Publish();
            }
            return new(true, Get());
        }
    }

    private SpeechConfigurationState Discover(SpeechSelection? selection, bool saved, string? error, long revision)
    {
        var providers = catalog.GetProviders().Where(item => item.IsInstalled
            && item.Id is SpeechProviderIds.Windows or SpeechProviderIds.Kokoro).ToArray();
        var voices = catalog.GetVoices().Where(voice => providers.Any(provider =>
            string.Equals(provider.Id, voice.ProviderId, StringComparison.Ordinal))).ToArray();
        var provider = providers.FirstOrDefault(item => string.Equals(item.Id, selection?.ProviderId, StringComparison.Ordinal));
        var voice = voices.FirstOrDefault(item => string.Equals(item.ProviderId, provider?.Id, StringComparison.Ordinal)
            && string.Equals(item.Id, selection?.VoiceId ?? provider?.DefaultVoiceId, StringComparison.Ordinal));
        return new(selection, error is null ? voice : null, Array.AsReadOnly(providers), Array.AsReadOnly(voices), revision, saved,
            error ?? (voice is null ? "The selected provider or voice is unavailable. Choose a ready installed choice or reset; visual responses remain available. Nothing is downloaded or substituted." : null));
    }

    internal async Task<string?> StartSummarySpeechAsync(string text, CallCommunicationPolicy policy,
        Func<bool> hostEligible, Func<Task> start, CancellationToken cancellationToken)
    {
        Task playback;
        lock (gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = Get();
            if (current.SummaryLimits is not { } limits)
            {
                SummarySpeechRefused(logger, "invalid-preferences");
                return current.SummaryLimitsRecovery;
            }
            var measured = SpokenSummaryMeasure.Count(text);
            if (!measured.Fits(limits))
            {
                SummarySpeechRefused(logger, "over-limit");
                return $"Speech withheld: the complete result is {measured.Sentences} sentences/{measured.Words} words, exceeding the configured {limits.Sentences}-sentence/{limits.Words}-word caps. No text was shortened; the full result is available visually.";
            }
            playback = policy.StartSpeech(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return start();
            }, hostEligible);
        }
        await playback;
        return null;
    }

    private static bool Equivalent(SpeechConfigurationState first, SpeechConfigurationState second) =>
        first.Selection == second.Selection && first.EffectiveVoice == second.EffectiveVoice
        && first.SummaryLimits == second.SummaryLimits && first.AreSummaryLimitsSaved == second.AreSummaryLimitsSaved
        && string.Equals(first.SummaryLimitsRecovery, second.SummaryLimitsRecovery, StringComparison.Ordinal)
        && first.IsSaved == second.IsSaved && string.Equals(first.Recovery, second.Recovery, StringComparison.Ordinal)
        && first.Providers.SequenceEqual(second.Providers) && first.Voices.SequenceEqual(second.Voices);

    private static void ValidateInitiator(SecurityAuditInitiator initiator)
    {
        if (initiator is not (SecurityAuditInitiator.LocalUser or SecurityAuditInitiator.TypedCommand or SecurityAuditInitiator.VoiceCommand))
        {
            throw new ArgumentOutOfRangeException(nameof(initiator));
        }
    }

    private void EnsureNotPublishing()
    {
        if (publishing) { throw new InvalidOperationException("Speech cannot be mutated during change notification."); }
    }

    private void Publish()
    {
        publishing = true;
        try { Changed?.Invoke(this, EventArgs.Empty); }
        finally { publishing = false; }
    }
}
