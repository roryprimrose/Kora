using Kora.Application.Configuration.SettingInspection;
using Kora.Core.Configuration;
using Kora.Core.Configuration.RegisteredSettings;

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
                var themeValue = new AppearanceValue.Theme(theme ?? ApplicationThemeMode.System);
                var scaleValue = new AppearanceValue.Number(scale ?? ReaderTextScaleSettings.DefaultPercent);
                AppearanceOptionRegistry.Validate(AppearanceOption.Theme, themeValue);
                AppearanceOptionRegistry.Validate(AppearanceOption.ReaderTextScale, scaleValue);
                if (values[AppearanceOption.Theme] != themeValue || values[AppearanceOption.ReaderTextScale] != scaleValue
                    || savedOptions.Contains(AppearanceOption.Theme) != theme.HasValue
                    || savedOptions.Contains(AppearanceOption.ReaderTextScale) != scale.HasValue)
                { return (SettingReadAvailability.Changed, []); }
                return (SettingReadAvailability.Confirmed,
                    [new(RegisteredSetting.Theme, (int)themeValue.GetTheme(), theme.HasValue, revision),
                     new(RegisteredSetting.ReaderTextScale, scaleValue.GetNumber(), scale.HasValue, revision)]);
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
