namespace Kora.Core.Skills;

internal static class SharedSkillFrontMatter
{
    // Deliberately a small versioned YAML subset, not a general object deserializer.
    // Anchors, tags, aliases, nesting and implicit types cannot acquire meaning.
    internal static Dictionary<string, string> Parse(string text, List<string> reasons)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var normalized = text.StartsWith('\uFEFF') ? text[1..] : text;
        var lines = normalized.Split('\n');
        if (lines.Length < 3 || !string.Equals(lines[0].TrimEnd('\r'), "---", StringComparison.Ordinal))
        {
            reasons.Add("missing-front-matter");
            return result;
        }
        var end = Array.FindIndex(lines, 1, line => string.Equals(line.TrimEnd('\r'), "---", StringComparison.Ordinal));
        if (end is < 0 or > 24 || lines.Take(end + 1).Sum(line => System.Text.Encoding.UTF8.GetByteCount(line) + 1) > 4096)
        {
            reasons.Add("invalid-or-oversized-front-matter");
            return result;
        }
        for (var index = 1; index < end; index++)
        {
            var line = lines[index].TrimEnd('\r');
            if (line.Length == 0) { continue; }
            var split = line.IndexOf(": ", StringComparison.Ordinal);
            if (split <= 0 || line.Length > 1100 || char.IsWhiteSpace(line[0]))
            {
                reasons.Add("unsupported-yaml-structure");
                continue;
            }
            var key = line[..split];
            var value = line[(split + 2)..];
            if (key.Any(c => c is not (>= 'a' and <= 'z' or '-'))
                || value.Length == 0 || value.Any(c => c is '\t' or '&' or '*' or '!' or '{' or '}' or '[' or ']' or '|'
                    or '>' or '#' or '`')
                || value.StartsWith('-') || value.StartsWith('?') || value.Contains(": ", StringComparison.Ordinal))
            {
                reasons.Add("unsupported-yaml-value");
                continue;
            }
            if (value.StartsWith('"') || value.StartsWith('\''))
            {
                if (value.Length < 2 || value[^1] != value[0]
                    || value[1..^1].Any(c => c is '"' or '\'' or '\\'))
                {
                    reasons.Add("unsupported-yaml-quoting");
                    continue;
                }
                value = value[1..^1];
            }
            else if (value.Contains('"', StringComparison.Ordinal) || value.Contains('\'', StringComparison.Ordinal))
            {
                reasons.Add("unsupported-yaml-quoting");
                continue;
            }
            if (!result.TryAdd(key, value)) { reasons.Add("duplicate-metadata"); }
            if (key is not ("name" or "version" or "description" or "allowed-tools"))
            { reasons.Add("unsupported-metadata"); }
        }
        return result;
    }
}
