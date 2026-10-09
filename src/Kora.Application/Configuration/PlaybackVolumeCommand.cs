using System.Text;
using Kora.Core.Commands;
using Kora.Core.Configuration;

namespace Kora.Application.Configuration;

public sealed record PlaybackVolumeCommand(AppearanceCommandOperation Operation, string? Value = null, string? Error = null)
{
    public const string OptionId = "speech.playback-volume";
    public const string Syntax = "list volume settings | get speech.playback-volume | status speech.playback-volume | "
        + "set speech.playback-volume to <integer 0-100> | reset speech.playback-volume. "
        + "Kora speech only; 0 blocks synthesis and keeps full visual output. Reset restores unscaled 100. Never replays stopped speech.";
    public static IReadOnlyList<string> FixedPhrases { get; } =
        ["list volume settings", "get " + OptionId, "status " + OptionId, "reset " + OptionId];

    public static PlaybackVolumeCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        {
            text = text[name.Length..].TrimStart(' ', ',', '\t');
        }
        if (!text.StartsWith("list volume", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("get speech.playback", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("status speech.playback", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("set speech.playback", StringComparison.OrdinalIgnoreCase)
            && !text.StartsWith("reset speech.playback", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        if (Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        {
            return new(AppearanceCommandOperation.Clarify, Error: "Volume input exceeds 1024 UTF-8 bytes or contains controls. " + Syntax);
        }
        if (text.Equals("list volume settings", StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.List); }
        if (text.Equals("get " + OptionId, StringComparison.OrdinalIgnoreCase)
            || text.Equals("status " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Get); }
        if (text.Equals("reset " + OptionId, StringComparison.OrdinalIgnoreCase)) { return new(AppearanceCommandOperation.Reset); }
        var prefix = "set " + OptionId + " to ";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && text.Length > prefix.Length)
        {
            return new(AppearanceCommandOperation.Set, text[prefix.Length..]);
        }
        return new(AppearanceCommandOperation.Clarify, Error: Syntax);
    }
}
