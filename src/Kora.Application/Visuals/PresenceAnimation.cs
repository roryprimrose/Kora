using Kora.Core;

namespace Kora.Application.Visuals;

public sealed class PresenceAnimation
{
    public static readonly TimeSpan FrameInterval = TimeSpan.FromMilliseconds(33);

    public static readonly TimeSpan VisibilityTransitionDuration = TimeSpan.FromMilliseconds(220);

    public static readonly TimeSpan ColorTransitionDuration = TimeSpan.FromMilliseconds(320);

    public static readonly TimeSpan SpeechScaleTransitionDuration = TimeSpan.FromMilliseconds(90);

    private const double MinimumSpeechScale = 0.9;
    private const double SpeechScaleRange = 0.22;

    public PresenceAnimation(AssistantState initialState)
    {
        Current = new PresenceVisualFrame(
            GetStateColor(initialState),
            initialState == AssistantState.Hidden ? 0 : 1,
            1);
    }

    public PresenceVisualFrame Current { get; private set; }

    public bool Advance(
        AssistantState state,
        bool isSpeaking,
        double speechOutputLevel,
        TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(elapsed), elapsed, "Elapsed time cannot be negative.");
        }

        if (isSpeaking && !double.IsFinite(speechOutputLevel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(speechOutputLevel),
                speechOutputLevel,
                "Speech output level must be finite.");
        }

        var targetColor = state == AssistantState.Hidden ? Current.Color : GetStateColor(state);
        var targetOpacity = state == AssistantState.Hidden ? 0 : 1;
        var targetScale = isSpeaking
            ? MinimumSpeechScale + (Math.Clamp(speechOutputLevel, 0, 1) * SpeechScaleRange)
            : 1;

        var next = new PresenceVisualFrame(
            MoveTowards(Current.Color, targetColor, elapsed, ColorTransitionDuration),
            MoveTowards(Current.Opacity, targetOpacity, elapsed, VisibilityTransitionDuration, 1),
            MoveTowards(Current.Scale, targetScale, elapsed, SpeechScaleTransitionDuration, SpeechScaleRange));
        var changed = next != Current;
        Current = next;
        return changed;
    }

    private static PresenceColor GetStateColor(AssistantState state) => state switch
    {
        AssistantState.Listening => new(0x6A, 0xE1, 0xDA),
        AssistantState.Calculating => new(0xAF, 0x9B, 0xFF),
        AssistantState.Waiting => new(0xF0, 0xCC, 0x83),
        AssistantState.Executing => new(0x80, 0xB7, 0xFF),
        AssistantState.Success => new(0x9B, 0xDF, 0xAC),
        AssistantState.Failure => new(0xF4, 0x9C, 0x9C),
        _ => new(0xB8, 0xD9, 0xEC),
    };

    private static PresenceColor MoveTowards(
        PresenceColor current,
        PresenceColor target,
        TimeSpan elapsed,
        TimeSpan duration)
    {
        var maximumDelta = (int)Math.Ceiling(byte.MaxValue * elapsed.TotalMilliseconds / duration.TotalMilliseconds);
        return new PresenceColor(
            MoveTowards(current.Red, target.Red, maximumDelta),
            MoveTowards(current.Green, target.Green, maximumDelta),
            MoveTowards(current.Blue, target.Blue, maximumDelta));
    }

    private static byte MoveTowards(byte current, byte target, int maximumDelta)
    {
        if (current < target)
        {
            return (byte)Math.Min(current + maximumDelta, target);
        }

        return (byte)Math.Max(current - maximumDelta, target);
    }

    private static double MoveTowards(
        double current,
        double target,
        TimeSpan elapsed,
        TimeSpan duration,
        double range)
    {
        var maximumDelta = range * elapsed.TotalMilliseconds / duration.TotalMilliseconds;
        if (current < target)
        {
            return Math.Min(current + maximumDelta, target);
        }

        return Math.Max(current - maximumDelta, target);
    }
}