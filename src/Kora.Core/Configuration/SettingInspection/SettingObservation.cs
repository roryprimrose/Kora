using System.Globalization;
using System.Text.Json;
using Kora.Core.Configuration.RegisteredSettings;
using Kora.Core.Voice;

namespace Kora.Core.Configuration.SettingInspection;

/// <summary>Closed validated preference values, never arbitrary preference text or runtime qualification.</summary>
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
            case RegisteredSetting.PresenceDisplay:
                if (value is not (0 or 1)) { throw new ArgumentOutOfRangeException(nameof(value)); }
                AppearanceOptionRegistry.Validate(AppearanceOption.PresenceDisplay, new AppearanceValue.Toggle(value == 1));
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

    public SettingObservation(SpeechSelection selection, bool saved, long revision)
    {
        ArgumentNullException.ThrowIfNull(selection);
        selection.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(revision);
        Setting = RegisteredSetting.SpeechVoice;
        Selection = selection;
        IsSaved = saved;
        Revision = revision;
    }

    public RegisteredSetting Setting { get; }
    public int? Value { get; }
    public SpeechSelection? Selection { get; }
    public bool IsSaved { get; }
    public long Revision { get; }
    public string Text => RegisteredSettingRegistry.Get(Setting).Id + " = " + (Selection is { } selection
        ? JsonSerializer.Serialize(selection.ProviderId) + " / "
            + (selection.VoiceId is { } voice ? JsonSerializer.Serialize(voice) : "provider-advertised default (exact voice not resolved)")
            + " (future-only; no runtime claim)"
        : ScalarText)
        + " " + RegisteredSettingRegistry.Get(Setting).Units
        + (IsSaved ? " (confirmed saved; matches live service)" : " (confirmed absent; live default)");

    private string ScalarText => Setting switch
    {
        RegisteredSetting.Theme => ((ApplicationThemeMode)Value!.Value).ToString(),
        RegisteredSetting.ResponseMode => ((ResponseOutputMode)Value!.Value).ToString(),
        RegisteredSetting.PresenceDisplay => Value == 1 ? "true" : "false",
        _ => Value!.Value.ToString(CultureInfo.InvariantCulture),
    };
}
