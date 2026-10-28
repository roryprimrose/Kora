using Kora.Core.Configuration.SettingInspection;

namespace Kora.Application.Configuration.SettingInspection;

public sealed class ConfirmedSettingsSource(AppearanceConfigurationService appearance, SpeechConfigurationService speech,
    PlaybackVolumeConfigurationService volume, ResponseModeConfigurationService response)
{
    public async Task<ConfirmedSettingsSnapshot> CaptureAsync(CancellationToken token)
    {
        var a = await appearance.ReadConfirmedSettingsAsync(token).ConfigureAwait(false);
        var s = await speech.ReadConfirmedSettingsAsync(token).ConfigureAwait(false);
        var v = await volume.ReadConfirmedSettingsAsync(token).ConfigureAwait(false);
        var r = await response.ReadConfirmedSettingsAsync(token).ConfigureAwait(false);
        var snapshot = new ConfirmedSettingsSnapshot(a, s, v, r);
        await ValidateAsync(snapshot, token).ConfigureAwait(false);
        return snapshot;
    }

    public bool IsCurrent(ConfirmedSettingsSnapshot snapshot) =>
        appearance.IsInspectionCurrent(snapshot.Appearance) && speech.IsInspectionCurrent(snapshot.Speech)
        && volume.IsInspectionCurrent(snapshot.Volume) && response.IsInspectionCurrent(snapshot.Response);

    public async Task ValidateAsync(ConfirmedSettingsSnapshot snapshot, CancellationToken token)
    {
        var a = await appearance.ReadConfirmedSettingsAsync(token).ConfigureAwait(false);
        var s = await speech.ReadConfirmedSettingsAsync(token).ConfigureAwait(false);
        var v = await volume.ReadConfirmedSettingsAsync(token).ConfigureAwait(false);
        var r = await response.ReadConfirmedSettingsAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!ReferenceEquals(snapshot.Appearance, a) || !ReferenceEquals(snapshot.Speech, s)
            || !ReferenceEquals(snapshot.Volume, v) || !ReferenceEquals(snapshot.Response, r) || !IsCurrent(snapshot))
        { throw new InvalidDataException("Confirmed setting source changed or is unavailable. Repair and explicitly inspect again."); }
    }
}
