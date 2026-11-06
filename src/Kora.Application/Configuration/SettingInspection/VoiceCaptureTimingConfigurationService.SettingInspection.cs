using Kora.Application.Configuration.SettingInspection;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed partial class VoiceCaptureTimingConfigurationService
{
    private readonly SettingReadTracker inspectionRead = new();

    public Task<ConfirmedSettingRead> ReadConfirmedSettingsAsync(CancellationToken token) => Task.Run(() =>
    {
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            return inspectionRead.Read(() =>
            {
                if (writing || timing is null)
                { return (SettingReadAvailability.NotObserved, []); }
                var observed = preferences.Load();
                var value = observed ?? VoiceCaptureTiming.Default;
                value.Validate();
                if (value != timing || (observed is not null) != (saved is not null))
                { return (SettingReadAvailability.Changed, []); }
                return (SettingReadAvailability.Confirmed,
                    [new(RegisteredSetting.CommandStartWait, value, observed is not null, revision),
                     new(RegisteredSetting.ReplyStartWait, value, observed is not null, revision),
                     new(RegisteredSetting.TrailingSilence, value, observed is not null, revision)]);
            });
        }
    }, token);

    internal bool IsInspectionCurrent(ConfirmedSettingRead read)
    {
        lock (gate)
        {
            return !writing && timing is not null && inspectionRead.IsCurrent(read)
                && read.Observations.All(item => item.Revision == revision);
        }
    }
}
