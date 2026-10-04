namespace Kora.Core.Voice;

public sealed record OptionalSpeechProviderOffer(
    string Title,
    string Detail,
    string ProviderId,
    bool IsRecovery);
