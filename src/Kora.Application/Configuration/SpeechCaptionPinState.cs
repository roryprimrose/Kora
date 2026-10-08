namespace Kora.Application.Configuration;

public sealed record SpeechCaptionPinState(bool Effective, bool Available)
{
    public int Schema => 1;
    public string Id => SpeechTextCommand.PinId;
    public string Type => "boolean";
    public bool Default => false;
    public IReadOnlyList<bool> Choices => [false, true];
    public string Source => "run-only";
    public string Scope => "run-only/current-caption";
    public string Effect => "ephemeral-local-presentation-only";
    public string ApplicationTiming => "current already-observed caption only; source and privacy retirement always wins";
    public string ResetEffect => "Unpin; preserve original completion deadline; never persist, speak or replay.";
    public string Syntax => SpeechTextCommand.Syntax;
}
