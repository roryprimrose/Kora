using System.Globalization;
using Kora.Core.Artifacts;
using Kora.Core.KnowledgeSources;

namespace Kora.Application.Configuration;

public sealed record KnowledgeSourceCaptureLimitsCommand(string Operation, string Name,
    KnowledgeSourceCaptureLimits? Limits = null, bool Invalid = false)
{
    public static KnowledgeSourceCaptureLimitsCommand? Parse(string input, string assistantName)
    {
        var text = ArtifactCommandRouter.RemoveActivationPrefix(input.TrimStart(), assistantName);
        const string prefix = "source capture limits ";
        if (!text.StartsWith("source capture limits", StringComparison.OrdinalIgnoreCase)) { return null; }
        if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { return InvalidCommand(); }
        var tail = text[prefix.Length..];
        if (tail.Equals("confirm", StringComparison.OrdinalIgnoreCase) || tail.Equals("cancel", StringComparison.OrdinalIgnoreCase))
        { return new(tail.ToLowerInvariant(), ""); }
        var separator = tail.IndexOf(" \"", StringComparison.Ordinal);
        if (separator < 0) { return InvalidCommand(); }
        var operation = tail[..separator].ToLowerInvariant();
        var end = tail.IndexOf('"', separator + 2);
        if (end < 0) { return InvalidCommand(); }
        var name = tail[(separator + 2)..end];
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.Any(char.IsControl)) { return InvalidCommand(); }
        var values = tail[(end + 1)..];
        if (operation is "get" or "reset" && values.Length == 0) { return new(operation, name); }
        if (!string.Equals(operation, "lower", StringComparison.Ordinal) || !values.StartsWith(' ')) { return InvalidCommand(); }
        var parts = values[1..].Split(' ');
        if (parts.Length != 3
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var count)
            || !long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var perFile)
            || !long.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var total))
        { return InvalidCommand(); }
        try { return new(operation, name, new(count, perFile, total)); }
        catch (InvalidDataException) { return InvalidCommand(); }
    }

    private static KnowledgeSourceCaptureLimitsCommand InvalidCommand() => new("", "", Invalid: true);
}
