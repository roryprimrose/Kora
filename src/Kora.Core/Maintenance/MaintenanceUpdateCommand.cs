using Kora.Core.Configuration;

namespace Kora.Core.Maintenance;

public sealed record MaintenanceUpdateCommand(bool Upgrade, ReleaseVersion? Version, bool IsValid = true)
{
    public static IReadOnlyList<string> FixedPhrases { get; } = ["maintenance check", "maintenance upgrade"];
    public const string Syntax = "Use maintenance check [latest|version] or maintenance upgrade [latest|version]. "
        + "Version must be major.minor.patch or major.minor.patch-betaN. Upgrade requires separate native installer approval.";

    public static MaintenanceUpdateCommand? Parse(string transcript, string assistantName)
    {
        var text = transcript.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        if (text.StartsWith(name, StringComparison.OrdinalIgnoreCase) && text.Length > name.Length
            && (char.IsWhiteSpace(text[name.Length]) || text[name.Length] == ','))
        { text = text[name.Length..].TrimStart(' ', ',', '\t'); }
        bool upgrade;
        if (text.Equals("maintenance upgrade", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("maintenance upgrade ", StringComparison.OrdinalIgnoreCase))
        { upgrade = true; text = text["maintenance upgrade".Length..].Trim(); }
        else if (text.Equals("maintenance check", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("maintenance check ", StringComparison.OrdinalIgnoreCase))
        { upgrade = false; text = text["maintenance check".Length..].Trim(); }
        else { return null; }
        if (transcript.Length > MaintenanceCommandParser.MaximumLength) { return new(upgrade, null, false); }
        if (text.Length == 0 || text.Equals("latest", StringComparison.OrdinalIgnoreCase)) { return new(upgrade, null); }
        try { return new(upgrade, ReleaseVersion.Parse(text)); }
        catch (InvalidDataException) { return new(upgrade, null, false); }
    }
}
