namespace Kora.Core.Configuration;

public static class SpeechOptionRegistry
{
    public const int SchemaVersion = 1;
    public static IReadOnlyList<SpeechOptionDescriptor> Options { get; } = Array.AsReadOnly(
    [
        new SpeechOptionDescriptor(SpeechOption.Provider, "speech.provider", "speech provider",
            Kora.Core.Voice.SpeechProviderIds.Windows, "Restores Windows and its advertised default voice."),
        new SpeechOptionDescriptor(SpeechOption.Voice, "speech.voice", "speech voice",
            "default", "Restores the selected provider's advertised default voice."),
    ]);

    public static SpeechOptionDescriptor Get(SpeechOption option) =>
        Options.Single(item => item.Option == option);
}
