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
            && MatchesLanguage(voice, profileCulture));

    public static bool MatchesLanguage(SpeechVoice voice, CultureInfo profileCulture)
    {
        ArgumentNullException.ThrowIfNull(voice);
        ArgumentNullException.ThrowIfNull(profileCulture);

        var language = profileCulture.TwoLetterISOLanguageName;
        return string.Equals(voice.Culture, language, StringComparison.OrdinalIgnoreCase)
            || voice.Culture.StartsWith($"{language}-", StringComparison.OrdinalIgnoreCase);
    }
}