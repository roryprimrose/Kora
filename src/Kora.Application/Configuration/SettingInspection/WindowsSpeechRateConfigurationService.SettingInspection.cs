using Kora.Application.Configuration.SettingInspection;
using Kora.Core.Configuration;
using Kora.Core.Configuration.SettingInspection;

namespace Kora.Application.Configuration;

public sealed partial class WindowsSpeechRateConfigurationService
{
    private readonly SettingReadTracker rateInspectionRead = new();

    internal Task<ConfirmedSettingRead> ReadConfirmedRateAsync(CancellationToken token) => Task.Run(() =>
    {
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            return rateInspectionRead.Read(() =>
            {
                if (applying || publishing || state.Desired is null || state.Source is not ("saved" or "default"))
                { return (SettingReadAvailability.NotObserved, []); }
                var saved = preferences.Load();
                var desired = saved ?? WindowsSpeechRate.Default;
                if (desired != state.Desired || (saved is not null) != string.Equals(state.Source, "saved", StringComparison.Ordinal))
                { return (SettingReadAvailability.Changed, []); }
                if (state.ProviderId is null) { return (SettingReadAvailability.NotObserved, []); }
                var rate = new WindowsSpeechRateObservation(desired.Value, state.Effective?.Value, saved is not null,
                    state.ProviderId, state.Support, state.ProviderRevision, state.Revision);
                return (SettingReadAvailability.Confirmed, [new(rate)]);
            });
        }
    }, token);

    internal bool IsRateInspectionCurrent(ConfirmedSettingRead read)
    {
        lock (gate)
        {
            return !applying && !publishing && state.Source is "saved" or "default"
                && rateInspectionRead.IsCurrent(read) && read.Observations.Single().Revision == state.Revision;
        }
    }
}
