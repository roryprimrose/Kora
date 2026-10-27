namespace Kora.Core.Configuration.RegisteredSettings;

/// <summary>The closed subset admitted for inert model suggestions, not a registry of write authority.</summary>
public static class RegisteredSettingRegistry
{
    public static IReadOnlyList<RegisteredSettingDescriptor> Settings { get; } = Array.AsReadOnly(
    [
        Appearance(RegisteredSetting.Theme, AppearanceOption.Theme),
        Appearance(RegisteredSetting.ReaderTextScale, AppearanceOption.ReaderTextScale),
        new(RegisteredSetting.SummaryWords, SpeechOptionRegistry.Get(SpeechOption.SummaryWords).Id,
            "future ordinary spoken summary word cap", "words", 1, SpokenSummaryLimits.MaximumWords, false),
        new(RegisteredSetting.SummarySentences, SpeechOptionRegistry.Get(SpeechOption.SummarySentences).Id,
            "future ordinary spoken summary sentence cap", "sentences", 1, SpokenSummaryLimits.MaximumSentences, false),
        new(RegisteredSetting.PlaybackVolume, "speech.playback-volume",
            "future Kora-only speech volume, never system/device/call volume", "percent",
            0, PlaybackVolume.MaximumPercent, false),
        new(RegisteredSetting.ResponseMode, "responses.default-mode",
            "future response output preference with mandatory full visual, privacy and call exceptions", "mode",
            null, null, false),
    ]);

    public static RegisteredSettingDescriptor Get(RegisteredSetting setting) =>
        Settings.Single(item => item.Setting == setting);

    private static RegisteredSettingDescriptor Appearance(RegisteredSetting setting, AppearanceOption option)
    {
        var descriptor = AppearanceOptionRegistry.Get(option);
        return new(setting, descriptor.Id, descriptor.Description, descriptor.Units,
            descriptor.Minimum, descriptor.Maximum, true);
    }
}
