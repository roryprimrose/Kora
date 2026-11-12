using Kora.Application.Presentation;
using Kora.Core.Artifacts;
using Kora.Core.Commands;
using Kora.Core.Tools;

namespace Kora.Application.Configuration;

public sealed record SettingsSectionCommand(SettingsSection? Section, string? Error = null, string? Filter = null)
{
    private static readonly string[] ReservedTargets =
        ["appearance", "speech", "audio", "microphone", "response", "session", "call"];
    public const string Syntax = "Use show setting <ID or search words>, view setting <ID or search words>, open appearance settings, open speech settings, open audio settings, open microphone settings, open response settings, open session settings, or open call settings. Navigation only; no setting or permission changes.";

    public static IReadOnlyList<string> FixedPhrases { get; } = Array.AsReadOnly(new[]
    {
        "open appearance settings", "open speech settings", "open audio settings", "open microphone settings",
        "open response settings", "open session settings", "open call settings",
    });

    public static SettingsSectionCommand? Parse(string transcript, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(transcript)) { return null; }
        if (WorkspaceSurfaceCommand.Parse(transcript, assistantName) is not null) { return null; }
        var text = ArtifactCommandRouter.RemoveActivationPrefix(transcript.Trim(), assistantName);
        foreach (var prefix in new[] { "show me the settings for", "show settings for", "show setting", "view setting", "open setting" })
        {
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                || text.Length > prefix.Length && !char.IsWhiteSpace(text[prefix.Length]))
            { continue; }
            var query = text[prefix.Length..].Trim();
            return SettingsNavigationTool.IsValidQuery(query) && !transcript.Any(char.IsControl)
                ? new(null, Filter: query) : new(null, Syntax);
        }
        if (text.StartsWith("show ", StringComparison.OrdinalIgnoreCase))
        {
            var id = text[5..].Trim();
            if (id.Contains('.', StringComparison.Ordinal)
                && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-'))
            {
                return SettingsNavigationTool.IsValidQuery(id) && !transcript.Any(char.IsControl)
                    ? new(null, Filter: id) : new(null, Syntax);
            }
        }
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
