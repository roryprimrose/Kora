using Kora.Application.Presentation;
using Kora.Core.Artifacts;

namespace Kora.Application.Configuration;

public sealed record SettingsSectionCommand(SettingsSection? Section, string? Error = null)
{
    private static readonly string[] ReservedTargets =
        ["appearance", "speech", "audio", "microphone", "response", "session", "call"];
    public const string Syntax = "Use open appearance settings, open speech settings, open audio settings, open microphone settings, open response settings, open session settings, or open call settings. Navigation only; no setting or permission changes.";

    public static IReadOnlyList<string> FixedPhrases { get; } = Array.AsReadOnly(new[]
    {
        "open appearance settings", "open speech settings", "open audio settings", "open microphone settings",
        "open response settings", "open session settings", "open call settings",
    });

    public static SettingsSectionCommand? Parse(string transcript, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(transcript)) { return null; }
        var text = ArtifactCommandRouter.RemoveActivationPrefix(transcript.Trim(), assistantName);
        if (string.Equals(text, "open settings", StringComparison.OrdinalIgnoreCase)) { return null; }
        if (!text.StartsWith("open", StringComparison.OrdinalIgnoreCase)
            || text.Length <= 4 || char.IsLetterOrDigit(text[4]))
        { return null; }
        var target = text[4..].TrimStart();
        if (!target.Contains("settings", StringComparison.OrdinalIgnoreCase)
            && !ReservedTargets.Any(name => target.StartsWith(name, StringComparison.OrdinalIgnoreCase)
                && (target.Length == name.Length || !char.IsLetterOrDigit(target[name.Length]))))
        { return null; }
        if (transcript.Length > 256 || transcript.Any(char.IsControl))
        { return new(null, Syntax); }
        return text.ToLowerInvariant() switch
        {
            "open appearance settings" => new(SettingsSection.Appearance),
            "open speech settings" or "open audio settings" or "open microphone settings" => new(SettingsSection.SpeechAudio),
            "open response settings" => new(SettingsSection.Responses),
            "open session settings" => new(SettingsSection.Sessions),
            "open call settings" => new(SettingsSection.Calls),
            _ => new(null, Syntax),
        };
    }
}
