using System.Text;
using Kora.Core.Configuration;

namespace Kora.Core.Context;

/// <summary>Prepares private literal data, never search execution or source selection authority.</summary>
public sealed class PreparedLocalSearchCommand
{
    private PreparedLocalSearchCommand(bool folder, string? query, bool activated)
    {
        IsFolder = folder;
        Query = query;
        IsActivated = activated;
    }

    public const string Syntax = "prepare file search for \"<query>\" | prepare folder search for \"<query>\". "
        + "Double a quote inside the final quoted literal. Review the exact admitted preview, then click native Search separately. "
        + "Preparation performs no read, search, refresh, attachment, model submission, clipboard or persistence.";

    public static IEnumerable<string> DiscoveryPhrases => ["prepare file search", "prepare folder search"];
    public bool IsFolder { get; }
    public string? Query { get; }
    public bool IsActivated { get; }
    public bool IsValid => Query is not null;
    public override string ToString() => nameof(PreparedLocalSearchCommand);

    public static PreparedLocalSearchCommand? Parse(string input, string assistantName)
    {
        var text = input.Trim();
        var name = AssistantNameRules.Normalize(assistantName);
        var activated = text.StartsWith(name, StringComparison.OrdinalIgnoreCase)
            && text.Length > name.Length && (text[name.Length] == ',' || char.IsWhiteSpace(text[name.Length]));
        if (activated) { text = text[name.Length..].TrimStart(' ', ',', '\t'); }
        if (!text.StartsWith("prepare", StringComparison.OrdinalIgnoreCase)
            || (text.Length > 7 && char.IsLetterOrDigit(text[7])))
        {
            return null;
        }
        var targetStart = 7;
        while (targetStart < text.Length && !char.IsLetterOrDigit(text[targetStart])) { targetStart++; }
        var target = text[targetStart..];
        if (!target.StartsWith("file", StringComparison.OrdinalIgnoreCase)
            && !target.StartsWith("folder", StringComparison.OrdinalIgnoreCase)
            && !"file".StartsWith(target, StringComparison.OrdinalIgnoreCase)
            && !"folder".StartsWith(target, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        PreparedLocalSearchCommand Invalid() => new(false, null, activated);
        // Bound the encoded literal too: every admitted quote can occupy two command characters.
        if (input.Length > LocalFileRetrievalPolicy.MaximumQueryCharacters * 2 + AssistantNameRules.MaximumLength + 64
            || input.Any(char.IsControl))
        {
            return Invalid();
        }
        const string filePrefix = "prepare file search for ";
        const string folderPrefix = "prepare folder search for ";
        var folder = text.StartsWith(folderPrefix, StringComparison.OrdinalIgnoreCase);
        var prefix = folder ? folderPrefix : filePrefix;
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { return Invalid(); }
        var literal = text[prefix.Length..];
        if (literal.Length < 2 || literal[0] != '"') { return Invalid(); }
        var value = new StringBuilder();
        var closed = false;
        for (var index = 1; index < literal.Length; index++)
        {
            if (literal[index] != '"') { value.Append(literal[index]); continue; }
            if (index + 1 < literal.Length && literal[index + 1] == '"') { value.Append('"'); index++; continue; }
            closed = index == literal.Length - 1;
            break;
        }
        if (!closed) { return Invalid(); }
        var query = value.ToString();
        try { LiteralMetadataQuery.Validate(query); }
        catch (InvalidDataException) { return Invalid(); }
        return LocalFileRetrievalPolicy.TryQuery(query, out _) ? new(folder, query, activated) : Invalid();
    }
}
