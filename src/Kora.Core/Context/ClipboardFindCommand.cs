using Kora.Core.Configuration;

namespace Kora.Core.Context;

/// <summary>Private literal preparation data, not capture, Find execution or source-selection authority.</summary>
public sealed class ClipboardFindCommand
{
    private ClipboardFindCommand(Guid snapshotId, string? query, bool activated)
    {
        SnapshotId = snapshotId;
        Query = query;
        IsActivated = activated;
    }

    public const string Syntax = "clipboard find <snapshot-id> <literal-query>. "
        + "Use the exact nonempty lowercase D-format snapshot ID from the native preview. "
        + "Everything after the single ID separator is literal, including spaces and quotes. "
        + "Preparation only: review the same snapshot and press native Find separately; no clipboard read or model use.";

    public Guid SnapshotId { get; }
    public string? Query { get; }
    public bool IsActivated { get; }
    public bool IsValid => Query is not null;
    public override string ToString() => nameof(ClipboardFindCommand);

    public static ClipboardFindCommand? Parse(string input, string assistantName)
    {
        var text = input.TrimStart();
        var name = AssistantNameRules.Normalize(assistantName);
        var activated = text.StartsWith(name, StringComparison.OrdinalIgnoreCase)
            && text.Length > name.Length && (text[name.Length] == ',' || char.IsWhiteSpace(text[name.Length]));
        if (activated)
        {
            var withoutName = text[name.Length..].TrimStart(' ', ',', '\t');
            activated = IsReserved(withoutName);
            if (activated) { text = withoutName; }
        }
        // Reserve malformed clipboard routes before generic echo, question answers or model dispatch.
        if (!IsReserved(text)) { return null; }
        ClipboardFindCommand Invalid() => new(Guid.Empty, null, activated);
        const string prefix = "clipboard find ";
        const int idLength = 36;
        if (input.Length > LocalFileRetrievalPolicy.MaximumQueryCharacters + AssistantNameRules.MaximumLength + 64
            || input.Any(char.IsControl) || !text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            || text.Length <= prefix.Length + idLength || text[prefix.Length + idLength] != ' ')
        {
            return Invalid();
        }
        var id = text.Substring(prefix.Length, idLength);
        if (!Guid.TryParseExact(id, "D", out var snapshotId) || snapshotId == Guid.Empty
            || !string.Equals(id, snapshotId.ToString("D"), StringComparison.Ordinal))
        {
            return Invalid();
        }
        var query = text[(prefix.Length + idLength + 1)..];
        return SnapshotLiteralFind.IsValidQuery(query) ? new(snapshotId, query, activated) : Invalid();
    }

    private static bool IsReserved(string text) =>
        text.StartsWith("clipboard", StringComparison.OrdinalIgnoreCase)
        && (text.Length <= 9 || !char.IsLetterOrDigit(text[9]));
}
