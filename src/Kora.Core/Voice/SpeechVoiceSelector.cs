using System.Globalization;

namespace Kora.Core.Voice;

public static class SpeechVoiceSelector
{
    public static SpeechVoice? SelectDefault(
        IEnumerable<SpeechVoice> voices,
        CultureInfo profileCulture)
    {
        ArgumentNullException.ThrowIfNull(voices);
        ArgumentNullException.ThrowIfNull(profileCulture);

        var availableVoices = voices.ToArray();

        return SelectForGender(availableVoices, profileCulture, SpeechVoiceGender.Female)
               ?? SelectForGender(availableVoices, profileCulture, SpeechVoiceGender.Male)
               ?? SelectForGender(availableVoices, profileCulture, SpeechVoiceGender.Neutral)
               ?? SelectForGender(availableVoices, profileCulture, SpeechVoiceGender.Unknown);
    }

    private static SpeechVoice? SelectForGender(
        IEnumerable<SpeechVoice> voices,
        CultureInfo profileCulture,
        SpeechVoiceGender gender) =>
        voices.FirstOrDefault(voice =>
            voice.Gender == gender
            && string.Equals(voice.Culture, profileCulture.Name, StringComparison.OrdinalIgnoreCase))
        ?? voices.FirstOrDefault(voice =>
            voice.Gender == gender
            && IsSameLanguage(voice.Culture, profileCulture.TwoLetterISOLanguageName));

    private static bool IsSameLanguage(string voiceCulture, string language) =>
        string.Equals(voiceCulture, language, StringComparison.OrdinalIgnoreCase)
        || voiceCulture.StartsWith($"{language}-", StringComparison.OrdinalIgnoreCase);
}