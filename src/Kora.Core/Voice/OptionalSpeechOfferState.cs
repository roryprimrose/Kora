namespace Kora.Core.Voice;

public sealed record OptionalSpeechOfferState(
    bool InitialOfferHandled,
    string? MissingProviderNotified);
