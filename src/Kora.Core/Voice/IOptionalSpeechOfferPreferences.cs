namespace Kora.Core.Voice;

public interface IOptionalSpeechOfferPreferences
{
    OptionalSpeechOfferState Load();

    void Save(OptionalSpeechOfferState state);
}
