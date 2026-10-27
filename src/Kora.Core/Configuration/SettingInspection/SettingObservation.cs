using System.Globalization;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Voice;

namespace Kora.Core.Configuration.SettingInspection;

/// <summary>Closed, non-sensitive scalar metadata, never arbitrary preference text.</summary>
public sealed record SettingObservation
{
    public SettingObservation(RegisteredSetting setting, int value, bool saved, long revision)
    {
        switch (setting)
        {
            case RegisteredSetting.Theme:
                AppearanceOptionRegistry.Validate(AppearanceOption.Theme, new AppearanceValue.Theme((ApplicationThemeMode)value));
                break;
            case RegisteredSetting.ReaderTextScale:
                AppearanceOptionRegistry.Validate(AppearanceOption.ReaderTextScale, new AppearanceValue.Number(value));
                break;
            case RegisteredSetting.SummaryWords:
                new SpokenSummaryLimits(1, value).Validate();
                break;
            case RegisteredSetting.SummarySentences:
                new SpokenSummaryLimits(value, 1).Validate();
                break;
            case RegisteredSetting.PlaybackVolume:
                _ = new PlaybackVolume(value);
                break;
            case RegisteredSetting.ResponseMode:
                _ = ResponseOutputModeResolver.Resolve((ResponseOutputMode)value, null, null);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(setting));
        }
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        Setting = setting;
        Value = value;
        IsSaved = saved;
        Revision = revision;
    }

    public RegisteredSetting Setting { get; }
    public int Value { get; }
    public bool IsSaved { get; }
    public long Revision { get; }
    public string Text => RegisteredSettingRegistry.Get(Setting).Id + " = " + (Setting switch
    {
        RegisteredSetting.Theme => ((ApplicationThemeMode)Value).ToString(),
        RegisteredSetting.ResponseMode => ((ResponseOutputMode)Value).ToString(),
        _ => Value.ToString(CultureInfo.InvariantCulture),
    }) + " " + RegisteredSettingRegistry.Get(Setting).Units
        + (IsSaved ? " (confirmed saved; matches live service)" : " (confirmed absent; live default)");
}
