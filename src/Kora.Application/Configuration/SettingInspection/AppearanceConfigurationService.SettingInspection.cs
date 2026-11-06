using System.Collections.Immutable;
using Kora.Application.Configuration.SettingInspection;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Configuration.SettingInspection;

namespace Kora.Application.Configuration;

public sealed partial class AppearanceConfigurationService
{
    private readonly SettingReadTracker inspectionRead = new();

    public Task<ConfirmedSettingRead> ReadConfirmedSettingsAsync(CancellationToken token) => Task.Run(() =>
    {
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            return inspectionRead.Read(() =>
            {
                if (!loaded || publishing) { return (SettingReadAvailability.NotObserved, []); }
                var theme = preferences.LoadThemeMode();
                var scale = preferences.LoadReaderTextScalePercent();
                var display = preferences.LoadPresenceDisplayEnabled();
                (RegisteredSetting Setting, AppearanceOption Option, AppearanceValue? Saved)[] observed =
                [
                    (RegisteredSetting.Theme, AppearanceOption.Theme, theme is { } t ? new AppearanceValue.Theme(t) : null),
                    (RegisteredSetting.ReaderTextScale, AppearanceOption.ReaderTextScale, scale is { } s ? new AppearanceValue.Number(s) : null),
                    (RegisteredSetting.PresenceDisplay, AppearanceOption.PresenceDisplay, display is { } d ? new AppearanceValue.Toggle(d) : null),
                    (RegisteredSetting.PresenceTimeout, AppearanceOption.PresenceTimeout,
                        preferences.LoadPresenceTimeoutSeconds() is { } timeout ? new AppearanceValue.Number(timeout) : null),
                    (RegisteredSetting.PresenceSize, AppearanceOption.PresenceSize,
                        preferences.LoadPresenceSizePixels() is { } size ? new AppearanceValue.Number(size) : null),
                    (RegisteredSetting.DotSize, AppearanceOption.DotSize,
                        preferences.LoadPresenceDotSizePercent() is { } dotSize ? new AppearanceValue.Number(dotSize) : null),
                    (RegisteredSetting.DotDensity, AppearanceOption.DotDensity,
                        preferences.LoadPresenceDotDensityPercent() is { } density ? new AppearanceValue.Number(density) : null),
                    (RegisteredSetting.MovementSpeed, AppearanceOption.MovementSpeed,
                        preferences.LoadPresenceMovementSpeedPercent() is { } speed ? new AppearanceValue.Number(speed) : null),
                    (RegisteredSetting.SpeechScaling, AppearanceOption.SpeechScaling,
                        preferences.LoadPresenceSpeechScalingEnabled() is { } scaling ? new AppearanceValue.Toggle(scaling) : null),
                    (RegisteredSetting.SpeechScaleAmount, AppearanceOption.SpeechScaleAmount,
                        preferences.LoadPresenceSpeechScaleAmountPercent() is { } amount ? new AppearanceValue.Number(amount) : null),
                    (RegisteredSetting.ReducedMotion, AppearanceOption.ReducedMotion,
                        preferences.LoadReducedMotionEnabled() is { } reduced ? new AppearanceValue.Toggle(reduced) : null),
                ];
                var observations = ImmutableArray.CreateBuilder<SettingObservation>();
                foreach (var item in observed)
                {
                    var value = item.Saved ?? AppearanceOptionRegistry.Get(item.Option).Default;
                    AppearanceOptionRegistry.Validate(item.Option, value);
                    if (values[item.Option] != value || savedOptions.Contains(item.Option) != (item.Saved is not null))
                    { return (SettingReadAvailability.Changed, []); }
                    var scalar = item.Option switch
                    {
                        AppearanceOption.Theme => (int)value.GetTheme(),
                        AppearanceOption.PresenceDisplay or AppearanceOption.SpeechScaling or AppearanceOption.ReducedMotion =>
                            value.GetToggle() ? 1 : 0,
                        _ => value.GetNumber(),
                    };
                    observations.Add(new(item.Setting, scalar, item.Saved is not null, revision));
                }
                return (SettingReadAvailability.Confirmed, observations.ToImmutable());
            });
        }
    }, token);

    internal bool IsInspectionCurrent(ConfirmedSettingRead read)
    {
        lock (gate)
        {
            return !publishing && loaded && inspectionRead.IsCurrent(read)
                && read.Observations.All(item => item.Revision == revision);
        }
    }
}
