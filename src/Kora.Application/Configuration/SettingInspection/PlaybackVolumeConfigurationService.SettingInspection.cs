using Kora.Application.Configuration.SettingInspection;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;

namespace Kora.Application.Configuration;

public sealed partial class PlaybackVolumeConfigurationService
{
    private readonly SettingReadTracker inspectionRead = new();

    public Task<ConfirmedSettingRead> ReadConfirmedSettingsAsync(CancellationToken token) => Task.Run(() =>
    {
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            return inspectionRead.Read(() =>
            {
                if (applying || publishing || !state.Available)
                { return (SettingReadAvailability.NotObserved, []); }
                var saved = preferences.Load();
                var value = saved ?? PlaybackVolume.Default;
                if (value != state.Desired || value != state.Effective
                    || !string.Equals(state.Source, saved is null ? "default" : "saved", StringComparison.Ordinal))
                { return (SettingReadAvailability.Changed, []); }
                return (SettingReadAvailability.Confirmed,
                    [new(RegisteredSetting.PlaybackVolume, value.Percent, saved.HasValue, state.Revision)]);
            });
        }
    }, token);

    internal bool IsInspectionCurrent(ConfirmedSettingRead read)
    {
        lock (gate)
        {
            return !applying && !publishing && state.Available && inspectionRead.IsCurrent(read)
                && read.Observations.All(item => item.Revision == state.Revision);
        }
    }
}
