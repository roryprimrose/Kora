using System.Globalization;

namespace Kora.Core.Voice;

public abstract record SpeechCaptionValue
{
    private SpeechCaptionValue() { }
    public sealed record Placement(SpeechCaptionPlacement Value) : SpeechCaptionValue;
    public sealed record Delay(int Seconds) : SpeechCaptionValue;
    public sealed record WordEmphasis(bool Enabled) : SpeechCaptionValue;
    public SpeechCaptionOption Option => this switch
    {
        Placement => SpeechCaptionOption.Placement,
        WordEmphasis => SpeechCaptionOption.WordEmphasis,
        _ => SpeechCaptionOption.DismissalDelay,
    };
    public string Label => this switch
    {
        Placement placement => placement.Value.ToString(),
        WordEmphasis emphasis => emphasis.Enabled ? "true" : "false",
        _ => ((Delay)this).Seconds.ToString(CultureInfo.InvariantCulture),
    };
}
