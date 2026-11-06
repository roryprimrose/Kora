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
            case RegisteredSetting.SpeechScaling:
            case RegisteredSetting.ReducedMotion:
                if (value is not (0 or 1)) { throw new ArgumentOutOfRangeException(nameof(value)); }
                AppearanceOptionRegistry.Validate(setting switch
                {
                    RegisteredSetting.SpeechScaling => AppearanceOption.SpeechScaling,
                    RegisteredSetting.ReducedMotion => AppearanceOption.ReducedMotion,
                    _ => AppearanceOption.PresenceDisplay,
                }, new AppearanceValue.Toggle(value == 1));
                break;
            case RegisteredSetting.PresenceTimeout:
                AppearanceOptionRegistry.Validate(AppearanceOption.PresenceTimeout, new AppearanceValue.Number(value));
                break;
            case RegisteredSetting.PresenceSize:
                AppearanceOptionRegistry.Validate(AppearanceOption.PresenceSize, new AppearanceValue.Number(value));
                break;
            case RegisteredSetting.DotSize:
                AppearanceOptionRegistry.Validate(AppearanceOption.DotSize, new AppearanceValue.Number(value));
                break;
            case RegisteredSetting.DotDensity:
                AppearanceOptionRegistry.Validate(AppearanceOption.DotDensity, new AppearanceValue.Number(value));
                break;
            case RegisteredSetting.MovementSpeed:
                AppearanceOptionRegistry.Validate(AppearanceOption.MovementSpeed, new AppearanceValue.Number(value));
                break;
            case RegisteredSetting.SpeechScaleAmount:
                AppearanceOptionRegistry.Validate(AppearanceOption.SpeechScaleAmount, new AppearanceValue.Number(value));
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
        + (IsSaved ? " (confirmed saved; matches live service)" : " (confirmed absent; live default)")
        + (Setting == RegisteredSetting.ReducedMotion
            ? Value == 0 ? "; follows Windows reduced-motion preference, not forced motion" : "; freezes presence particles"
            : "");

    private string ScalarText => Setting switch
    {
        RegisteredSetting.Theme => ((ApplicationThemeMode)Value!.Value).ToString(),
        RegisteredSetting.ResponseMode => ((ResponseOutputMode)Value!.Value).ToString(),
        RegisteredSetting.PresenceDisplay or RegisteredSetting.SpeechScaling or RegisteredSetting.ReducedMotion =>
            Value == 1 ? "true" : "false",
        _ => Value!.Value.ToString(CultureInfo.InvariantCulture),
    };
}
