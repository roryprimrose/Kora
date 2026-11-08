using Kora.Application.Configuration.SettingInspection;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Configuration.SettingInspection;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed partial class SpeechConfigurationService
{
    private readonly SettingReadTracker inspectionRead = new();
    private readonly SettingReadTracker rateSelectionRead = new();

    internal Task<ConfirmedSettingRead> ReadConfirmedRateSelectionAsync(CancellationToken token) => Task.Run(() =>
    {
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            return rateSelectionRead.Read(() =>
            {
                if (publishing || state?.Selection is null || heldRecovery is not null)
                { return (SettingReadAvailability.NotObserved, []); }
                var persisted = preferences.LoadSelection();
                var selection = persisted ?? SpeechSelection.Default;
                selection.Validate();
                if (selection != state.Selection || (persisted is not null) != state.IsSaved)
                { return (SettingReadAvailability.Changed, []); }
                return (SettingReadAvailability.Confirmed, [new(selection, persisted is not null, state.Revision)]);
            });
        }
    }, token);

    internal bool IsRateSelectionCurrent(ConfirmedSettingRead read, WindowsSpeechRateObservation rate)
    {
        lock (gate)
        {
            if (publishing || heldRecovery is not null || state?.Selection is null
                || !rateSelectionRead.IsCurrent(read) || read.Observations.Single().Revision != state.Revision
                || state.Revision != rate.ProviderRevision || !string.Equals(state.Selection.ProviderId, rate.ProviderId, StringComparison.Ordinal))
            { return false; }
            var providers = state.Providers.Where(provider => string.Equals(provider.Id, rate.ProviderId, StringComparison.Ordinal)).ToArray();
            var support = state.IsAvailable && providers.Length == 1 && providers[0].IsInstalled
                && string.Equals(rate.ProviderId, SpeechProviderIds.Windows, StringComparison.Ordinal)
                && providers[0].RateSupport == SpeechRateSupport.WindowsNative
                ? SpeechRateSupport.WindowsNative : SpeechRateSupport.Unsupported;
            return support == rate.Support;
        }
    }

    public Task<ConfirmedSettingRead> ReadConfirmedSettingsAsync(CancellationToken token) => Task.Run(() =>
    {
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            return inspectionRead.Read(() =>
            {
                if (publishing || state?.SummaryLimits is null || state.SummaryLimitsRecovery is not null
                    || state.Selection is null || heldRecovery is not null)
                { return (SettingReadAvailability.NotObserved, []); }
                var persisted = preferences.LoadSelection();
                var selection = persisted ?? SpeechSelection.Default;
                selection.Validate();
                var saved = preferences.LoadSummaryLimits();
                var limits = saved ?? SpokenSummaryLimits.Default;
                limits.Validate();
                if (limits != state.SummaryLimits || (saved is not null) != state.AreSummaryLimitsSaved
                    || selection != state.Selection || (persisted is not null) != state.IsSaved)
                { return (SettingReadAvailability.Changed, []); }
                return (SettingReadAvailability.Confirmed,
                    [new(RegisteredSetting.SummaryWords, limits.Words, saved is not null, state.Revision),
                     new(RegisteredSetting.SummarySentences, limits.Sentences, saved is not null, state.Revision),
                     new(selection, persisted is not null, state.Revision)]);
            });
        }
    }, token);

    internal bool IsInspectionCurrent(ConfirmedSettingRead read)
    {
        lock (gate)
        {
            return !publishing && inspectionRead.IsCurrent(read)
                && read.Observations.All(item => item.Revision == state?.Revision);
        }
    }
}
