namespace Kora.Core.Dependencies;

public sealed record SpeechComponentReadiness(SpeechComponentIdentity Identity, SpeechComponentSnapshot Storage,
    bool Compatible, bool Qualified, bool FunctionalVerified = false)
{
    public bool CanOffer => Compatible && Qualified;
}
