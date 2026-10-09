namespace Kora.Core.Configuration;

public static class SpeechOptionRegistry
{
    public const int SchemaVersion = 2;
    public static IReadOnlyList<SpeechOptionDescriptor> Options { get; } = Array.AsReadOnly(
    [
        new SpeechOptionDescriptor(SpeechOption.Provider, "speech.provider", "speech provider",
            Kora.Core.Voice.SpeechProviderIds.Windows, "Restores Windows and its advertised default voice."),
        new SpeechOptionDescriptor(SpeechOption.Voice, "speech.voice", "speech voice",
            "default", "Restores the selected provider's advertised default voice."),
        new SpeechOptionDescriptor(SpeechOption.SummarySentences, "speech.summary-sentences", "speech summary sentences",
            "3", "Restores the default 3-sentence cap; preserves the word cap."),
        new SpeechOptionDescriptor(SpeechOption.SummaryWords, "speech.summary-words", "speech summary words",
            "80", "Restores the default 80-word cap; preserves the sentence cap."),
    ]);

    public static SpeechOptionDescriptor Get(SpeechOption option) =>
        Options.Single(item => item.Option == option);
}
