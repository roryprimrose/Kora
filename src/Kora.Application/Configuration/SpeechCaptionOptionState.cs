namespace Kora.Application.Configuration;

public sealed record SpeechCaptionOptionState(string Id, string Type, string Units, string? Saved,
    string? Effective, string Default, string Source, bool Available, IReadOnlyList<string> Choices,
    int? Minimum, int? Maximum, long Revision, string Outcome, string? Recovery)
{
    public int Schema => 1;
    public string Scope => "device-local";
    public string Effect => "local-current-playback-presentation-only";
    public string ApplicationTiming => "next eligible playback only; old captions retired immediately";
    public string ResetEffect => "Saves only this option's default; does not enable captions or change the companion option.";
    public string Syntax => SpeechTextCommand.Syntax;
}
