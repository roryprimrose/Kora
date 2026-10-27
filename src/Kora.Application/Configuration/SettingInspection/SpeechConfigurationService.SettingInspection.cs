using Kora.Application.Configuration.SettingInspection;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;

namespace Kora.Application.Configuration;

public sealed partial class SpeechConfigurationService
{
    private readonly SettingReadTracker inspectionRead = new();

    public Task<ConfirmedSettingRead> ReadConfirmedSettingsAsync(CancellationToken token) => Task.Run(() =>
    {
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            return inspectionRead.Read(() =>
            {
                if (publishing || state?.SummaryLimits is null || state.SummaryLimitsRecovery is not null)
                { return (SettingReadAvailability.NotObserved, []); }
                var saved = preferences.LoadSummaryLimits();
                var limits = saved ?? SpokenSummaryLimits.Default;
                limits.Validate();
                if (limits != state.SummaryLimits || (saved is not null) != state.AreSummaryLimitsSaved)
                { return (SettingReadAvailability.Changed, []); }
                return (SettingReadAvailability.Confirmed,
                    [new(RegisteredSetting.SummaryWords, limits.Words, saved is not null, state.Revision),
                     new(RegisteredSetting.SummarySentences, limits.Sentences, saved is not null, state.Revision)]);
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
