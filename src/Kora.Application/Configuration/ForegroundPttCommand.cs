using Kora.Core.Artifacts;
using Kora.Core.Voice;

namespace Kora.Application.Configuration;

public sealed record ForegroundPttCommand(AppearanceCommandOperation Operation, ForegroundPttShortcut? Shortcut = null, string? Error = null)
{
    public const string Syntax = "Use get push-to-talk shortcut, set push-to-talk to Control Shift Space, set push-to-talk to Control Shift F9, or reset push-to-talk shortcut. Foreground Response window only; other editing, navigation and system chords are reserved. Saving does not enable listening.";
    public static IReadOnlyList<string> FixedPhrases { get; } = Array.AsReadOnly(new[]
    {
        "get push-to-talk shortcut", "set push-to-talk to Control Shift Space",
        "set push-to-talk to Control Shift F9", "reset push-to-talk shortcut",
    });

    public static ForegroundPttCommand? Parse(string transcript, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(transcript)) { return null; }
        var text = ArtifactCommandRouter.RemoveActivationPrefix(transcript.Trim(), assistantName);
        if (!text.Contains("push-to-talk", StringComparison.OrdinalIgnoreCase)) { return null; }
        if (transcript.Length > 256 || transcript.Any(char.IsControl))
        { return new(AppearanceCommandOperation.Clarify, Error: Syntax); }
        if (text.Equals("get push-to-talk shortcut", StringComparison.OrdinalIgnoreCase))
        { return new(AppearanceCommandOperation.Get); }
        if (text.Equals("reset push-to-talk shortcut", StringComparison.OrdinalIgnoreCase))
        { return new(AppearanceCommandOperation.Reset, ForegroundPttShortcut.Unassigned); }
        const string prefix = "set push-to-talk to ";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && ForegroundPttShortcut.Choices.SingleOrDefault(choice =>
                choice.Label.Equals(text[prefix.Length..], StringComparison.OrdinalIgnoreCase)) is { } shortcut)
        { return new(AppearanceCommandOperation.Set, shortcut); }
        return new(AppearanceCommandOperation.Clarify, Error: Syntax);
    }
}
