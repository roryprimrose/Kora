using System.Text;
using Kora.Core.Commands;
using Kora.Core.Configuration;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed record VoiceCaptureTimingCommand(AppearanceCommandOperation Operation, string? Option = null,
    string? Value = null, bool? ReviewDecision = null)
{
    public const string Syntax = "get voice.timing | set voice.command-start-wait to <2-10 seconds> | "
        + "set voice.reply-start-wait to <5-60 seconds> | set voice.trailing-silence to <0.5-3 seconds> | "
        + "reset <exact timing option>. Values are decimal seconds without a suffix. Review, then confirm voice.timing or cancel voice.timing.";
    public static IReadOnlyList<string> OptionIds { get; } =
        [VoiceCaptureTiming.CommandStartId, VoiceCaptureTiming.ReplyStartId, VoiceCaptureTiming.TrailingSilenceId];
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["get voice.timing", "status voice.timing", "list voice timing settings", "confirm voice.timing", "cancel voice.timing",
            "reset " + VoiceCaptureTiming.CommandStartId, "reset " + VoiceCaptureTiming.ReplyStartId, "reset " + VoiceCaptureTiming.TrailingSilenceId];

    public static VoiceCaptureTimingCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        { text = text[name.Length..].TrimStart(' ', ',', '\t'); }
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (!text.Equals("list voice timing settings", StringComparison.OrdinalIgnoreCase)
            && (parts.Length < 2 || !(parts[1].Equals("voice.timing", StringComparison.OrdinalIgnoreCase)
                || OptionIds.Contains(parts[1], StringComparer.OrdinalIgnoreCase))))
        { return null; }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        { return new(AppearanceCommandOperation.Clarify); }
        if (text.Equals("list voice timing settings", StringComparison.OrdinalIgnoreCase))
        { return new(AppearanceCommandOperation.List); }
        if (parts.Length == 2)
        {
            if (parts[0].Equals("get", StringComparison.OrdinalIgnoreCase) || parts[0].Equals("status", StringComparison.OrdinalIgnoreCase))
            { return new(AppearanceCommandOperation.Get); }
            if (parts[1].Equals("voice.timing", StringComparison.OrdinalIgnoreCase))
            {
                if (parts[0].Equals("confirm", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Set, ReviewDecision: true); }
                if (parts[0].Equals("cancel", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Set, ReviewDecision: false); }
            }
            else if (parts[0].Equals("reset", StringComparison.OrdinalIgnoreCase))
            { return new(AppearanceCommandOperation.Reset, parts[1].ToLowerInvariant()); }
        }
        if (parts.Length == 4 && parts[0].Equals("set", StringComparison.OrdinalIgnoreCase)
            && parts[2].Equals("to", StringComparison.OrdinalIgnoreCase) && OptionIds.Contains(parts[1], StringComparer.OrdinalIgnoreCase))
        { return new(AppearanceCommandOperation.Set, parts[1].ToLowerInvariant(), parts[3]); }
        return new(AppearanceCommandOperation.Clarify);
    }
}
