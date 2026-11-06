namespace Kora.Core.Voice;

public sealed record SpeechCaptionOptions
{
    public const int MinimumDelaySeconds = 0;
    public const int MaximumDelaySeconds = 30;
    public const int DefaultDelaySeconds = 5;
    public static SpeechCaptionOptions Default { get; } = new(SpeechCaptionPlacement.BottomRight, DefaultDelaySeconds);

    public SpeechCaptionOptions(SpeechCaptionPlacement placement, int dismissalDelaySeconds, bool wordEmphasis = false)
    {
        if (!Enum.IsDefined(placement)) { throw new ArgumentOutOfRangeException(nameof(placement)); }
        if (dismissalDelaySeconds is < MinimumDelaySeconds or > MaximumDelaySeconds)
        {
            throw new ArgumentOutOfRangeException(nameof(dismissalDelaySeconds));
        }
        Placement = placement;
        DismissalDelaySeconds = dismissalDelaySeconds;
        WordEmphasis = wordEmphasis;
    }

    public SpeechCaptionPlacement Placement { get; }
    public int DismissalDelaySeconds { get; }
    public bool WordEmphasis { get; }
    public SpeechCaptionOptions With(SpeechCaptionValue value) => value switch
    {
        SpeechCaptionValue.Placement placement => new(placement.Value, DismissalDelaySeconds, WordEmphasis),
        SpeechCaptionValue.Delay delay => new(Placement, delay.Seconds, WordEmphasis),
        SpeechCaptionValue.WordEmphasis emphasis => new(Placement, DismissalDelaySeconds, emphasis.Enabled),
        _ => throw new ArgumentOutOfRangeException(nameof(value)),
    };
}
