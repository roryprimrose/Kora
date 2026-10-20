using System.Text;

using Kora.Core.Artifacts;
using Kora.Core.Hosting;

namespace Kora.Core.Commands;

/// <summary>An exact future list predicate, never a resolved session or navigation authority.</summary>
public sealed class SessionListPreparationCommand
{
    private SessionListPreparationCommand(Guid? sessionId, bool help, bool activated, bool valid)
    {
        SessionId = sessionId;
        IsHelp = help;
        IsActivated = activated;
        IsValid = valid;
    }

    public Guid? SessionId { get; }
    public bool IsHelp { get; }
    public bool IsActivated { get; }
    public bool IsValid { get; }
    public override string ToString() => nameof(SessionListPreparationCommand);
    public static IReadOnlyList<string> DiscoveryPhrases { get; } = ["prepare session list", "session list help"];
    public const string Syntax = "prepare session list <session-id> | session list help. "
        + "Use a nonempty canonical lowercase D GUID, never a name, current or latest. "
        + "Preparation only fills the native ExactId metadata query; no Search has run. "
        + "All existing lifecycle, name availability, generation, name-revision and name-length filter drafts are retained and AND together. "
        + "Review every filter, then explicitly Search and select/navigate an exact native result separately. "
        + "The ID does not prove existence, absence, ownership or permission. No body read, count, selection, resume, activity renewal or action authority.";

    public static SessionListPreparationCommand? Parse(string input, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(input)) { return null; }
        var text = input.Trim();
        var activated = false;
        var stripped = ArtifactCommandRouter.RemoveActivationPrefix(text, assistantName);
        if (Reserved(stripped))
        {
            activated = !string.Equals(text, stripped, StringComparison.Ordinal);
            text = stripped;
        }
        if (!Reserved(text)) { return null; }
        SessionListPreparationCommand Invalid() => new(null, false, activated, false);
        if (input.Length > SessionCommand.MaximumInputBytes
            || Encoding.UTF8.GetByteCount(input) > SessionCommand.MaximumInputBytes || input.Any(char.IsControl))
        { return Invalid(); }
        if (text.Equals("session list help", StringComparison.OrdinalIgnoreCase))
        { return new(null, true, activated, true); }
        const string prefix = "prepare session list ";
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { return Invalid(); }
        Guid id;
        try { id = OptionalCanonicalGuid.ParseRequired(text[prefix.Length..]); }
        catch (InvalidDataException) { return Invalid(); }
        return new(id, false, activated, true);
    }

    private static bool Reserved(string text)
    {
        var family = BuiltInCommandRouter.Normalize(text[..Math.Min(text.Length, 128)]);
        return family.StartsWith("prepare session", StringComparison.Ordinal)
            || family.StartsWith("session list help", StringComparison.Ordinal);
    }
}
