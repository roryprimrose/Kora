using System.Globalization;

namespace Kora.Core.Voice;

public abstract record SpeechCaptionValue
{
    private SpeechCaptionValue() { }
    public sealed record Placement(SpeechCaptionPlacement Value) : SpeechCaptionValue;
    public sealed record Delay(int Seconds) : SpeechCaptionValue;
    public SpeechCaptionOption Option => this is Placement ? SpeechCaptionOption.Placement : SpeechCaptionOption.DismissalDelay;
    public string Label => this is Placement placement ? placement.Value.ToString()
        : ((Delay)this).Seconds.ToString(CultureInfo.InvariantCulture);
}
