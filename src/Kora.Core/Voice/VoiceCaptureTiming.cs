using System.Globalization;

namespace Kora.Core.Voice;

/// <summary>Device-local timing for the next eligible capture, never listening authority.</summary>
public sealed record VoiceCaptureTiming(decimal CommandStartSeconds, decimal ReplyStartSeconds, decimal TrailingSilenceSeconds)
{
    public const string CommandStartId = "voice.command-start-wait";
    public const string ReplyStartId = "voice.reply-start-wait";
    public const string TrailingSilenceId = "voice.trailing-silence";
    public static VoiceCaptureTiming Default { get; } = new(5, 15, 1);
    public TimeSpan CommandStart => ToDuration(CommandStartSeconds);
    public TimeSpan ReplyStart => ToDuration(ReplyStartSeconds);
    public TimeSpan TrailingSilence => ToDuration(TrailingSilenceSeconds);

    public void Validate()
    {
        RequireRange(CommandStartSeconds, 2, 10, nameof(CommandStartSeconds));
        RequireRange(ReplyStartSeconds, 5, 60, nameof(ReplyStartSeconds));
        RequireRange(TrailingSilenceSeconds, 0.5m, 3, nameof(TrailingSilenceSeconds));
    }

    public VoiceCaptureTiming With(string id, string? seconds)
    {
        var defaults = Default;
        var value = seconds is null ? id switch
        {
            CommandStartId => defaults.CommandStartSeconds,
            ReplyStartId => defaults.ReplyStartSeconds,
            TrailingSilenceId => defaults.TrailingSilenceSeconds,
            _ => throw new ArgumentOutOfRangeException(nameof(id)),
        } : ParseSeconds(seconds);
        var next = id switch
        {
            CommandStartId => this with { CommandStartSeconds = value },
            ReplyStartId => this with { ReplyStartSeconds = value },
            TrailingSilenceId => this with { TrailingSilenceSeconds = value },
            _ => throw new ArgumentOutOfRangeException(nameof(id)),
        };
        next.Validate();
        return next;
    }

    public string Serialize()
    {
        Validate();
        return string.Join('\n', "1", Format(CommandStartSeconds), Format(ReplyStartSeconds), Format(TrailingSilenceSeconds));
    }

    public static VoiceCaptureTiming Parse(string contents)
    {
        var lines = contents.Split('\n');
        if (lines.Length != 4 || !string.Equals(lines[0], "1", StringComparison.Ordinal))
        { throw new InvalidDataException("The saved capture timing format is unknown or malformed."); }
        try
        {
            var value = new VoiceCaptureTiming(ParseSeconds(lines[1]), ParseSeconds(lines[2]), ParseSeconds(lines[3]));
            value.Validate();
            return value;
        }
        catch (ArgumentException exception)
        { throw new InvalidDataException("Saved capture timing must contain supported seconds within the exact bounds.", exception); }
    }

    public string Describe() => $"command start {Format(CommandStartSeconds)} s; reply start {Format(ReplyStartSeconds)} s; trailing silence {Format(TrailingSilenceSeconds)} s";

    private static string Format(decimal value) => value.ToString("0.#######", CultureInfo.InvariantCulture);
    private static TimeSpan ToDuration(decimal seconds) => TimeSpan.FromTicks(decimal.ToInt64(seconds * TimeSpan.TicksPerSecond));
    private static decimal ParseSeconds(string value) =>
        decimal.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var seconds)
            ? seconds : throw new ArgumentException("Use an exact decimal number of seconds with a dot, without a suffix.", nameof(value));
    private static void RequireRange(decimal value, decimal minimum, decimal maximum, string name)
    {
        if (value < minimum || value > maximum || decimal.Truncate(value * TimeSpan.TicksPerSecond) != value * TimeSpan.TicksPerSecond)
        { throw new ArgumentOutOfRangeException(name, $"Use {minimum} to {maximum} seconds, at most seven fractional digits; values are never clamped."); }
    }
}
