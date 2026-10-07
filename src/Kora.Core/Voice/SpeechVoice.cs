namespace Kora.Core.Voice;

public sealed record SpeechVoice(
    string Id,
    string Name,
    string Culture,
    SpeechVoiceGender Gender)
{
    public string ProviderId { get; init; } = SpeechProviderIds.Windows;

    public string ConfigurationId => $"{ProviderId} / {Id}";
}