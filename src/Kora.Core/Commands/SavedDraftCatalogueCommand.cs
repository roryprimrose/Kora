using Kora.Core.Artifacts;
using Kora.Core.Hosting;

namespace Kora.Core.Commands;

public sealed record SavedDraftCatalogueCommand(Guid? SessionId, bool IsHelp = false, bool IsActivated = false)
{
    public const string PreparePhrase = "prepare saved draft catalogue";
    public const string HelpPhrase = "saved draft catalogue help";
    public const string Help = "Use prepare saved draft catalogue <session-id> with a nonempty canonical lowercase D-format GUID. "
        + "Preparation only: ExactId and query ID are set in the native saved conversation draft metadata catalogue. "
        + "All other visible filter drafts are retained and AND-composed with the ID. Review them and explicitly Search. "
        + "No metadata or body is read, no session is resolved or selected, and no permission or current ownership is established. "
        + "An ID is not proof that a session or saved draft exists. Exact session navigation remains a separate guarded action.";

    public static SavedDraftCatalogueCommand? Parse(string input, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(input)) { return null; }
        var text = input.Trim();
        var activated = false;
        // A fixed command wins over an assistant name that collides with its family.
        if (!text.StartsWith(PreparePhrase + " ", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, HelpPhrase, StringComparison.OrdinalIgnoreCase))
        {
            var withoutPrefix = ArtifactCommandRouter.RemoveActivationPrefix(text, assistantName);
            activated = !string.Equals(text, withoutPrefix, StringComparison.Ordinal);
            text = withoutPrefix;
        }
        var normalized = BuiltInCommandRouter.Normalize(text);
        if (normalized.Length == 0) { return null; }
        if (!IsFamily(normalized))
        {
            return IsFamily(ArtifactCommandRouter.RemoveActivationPrefix(normalized, assistantName))
                ? new(null, IsActivated: activated) : null;
        }
        if (input.Any(char.IsControl) || input.Length != input.TrimEnd().Length
            || text.Length > PreparePhrase.Length + 1 + 36) { return new(null, IsActivated: activated); }
        if (string.Equals(text, HelpPhrase, StringComparison.OrdinalIgnoreCase)) { return new(null, true, activated); }
        if (!text.StartsWith(PreparePhrase + " ", StringComparison.OrdinalIgnoreCase)) { return new(null, IsActivated: activated); }
        try { return new(OptionalCanonicalGuid.Parse(text[(PreparePhrase.Length + 1)..]), IsActivated: activated); }
        catch (InvalidDataException) { return new(null, IsActivated: activated); }
    }

    private static bool IsFamily(string text) =>
        text.StartsWith("prepare saved draft", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("saved draft catalogue", StringComparison.OrdinalIgnoreCase);
}
