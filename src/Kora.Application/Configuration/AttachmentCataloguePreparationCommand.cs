using Kora.Core.Artifacts;
using Kora.Core.Storage;

namespace Kora.Application.Configuration;

/// <summary>Reserved navigation syntax; an ID is future query data, never session authority.</summary>
public sealed record AttachmentCataloguePreparationCommand(Guid? SessionId, bool IsHelp, bool IsActivated)
{
    private const string Prefix = "prepare attachment catalogue ";
    public const string Syntax = "prepare attachment catalogue <session-id> | attachment catalogue help. "
        + "Use one nonempty canonical lowercase D-format session ID. Preparation only: no I/O, search, count, "
        + "session selection, capture or read. Review all visible filters, which remain AND-composed, then explicitly Search.";

    public static IReadOnlyList<string> DiscoveryPhrases { get; } =
        Array.AsReadOnly(new[] { "prepare attachment catalogue", "attachment catalogue help" });

    public static AttachmentCataloguePreparationCommand? Parse(string input, string assistantName)
    {
        if (string.IsNullOrWhiteSpace(input)) { return null; }
        var text = input.Trim(' ');
        // Whole reserved phrases win over a colliding configured assistant name.
        var activated = false;
        if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(text, "attachment catalogue help", StringComparison.OrdinalIgnoreCase))
        {
            var without = ArtifactCommandRouter.RemoveActivationPrefix(text, assistantName);
            if (Reserved(without))
            {
                activated = !string.Equals(text, without, StringComparison.Ordinal);
                text = without;
            }
        }
        if (!Reserved(text)) { return null; }
        AttachmentCataloguePreparationCommand Invalid() => new(null, false, activated);
        if (input.Length > 256 || input.Any(char.IsControl)
            || text.Any(character => character < ' ' || character > '~'))
        { return Invalid(); }
        if (string.Equals(text, "attachment catalogue help", StringComparison.OrdinalIgnoreCase))
        { return new(null, true, activated); }
        if (!text.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) { return Invalid(); }
        var id = text[Prefix.Length..];
        return SessionListSearch.IsValid(SessionListSearchKind.ExactId, SessionListFilter.All, id)
            ? new(Guid.ParseExact(id, "D"), false, activated) : Invalid();
    }

    private static bool Reserved(string text)
    {
        // Reservation tolerates unsafe separators only to refuse them, never to admit repaired syntax.
        var family = new string(text.Take(256).Where(char.IsAsciiLetterOrDigit).ToArray());
        return family.StartsWith("prepareattachment", StringComparison.OrdinalIgnoreCase)
            || family.StartsWith("attachmentcatalogue", StringComparison.OrdinalIgnoreCase);
    }
}
