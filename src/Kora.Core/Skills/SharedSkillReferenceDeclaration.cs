using System.Collections.ObjectModel;

namespace Kora.Core.Skills;

/// <summary>A finite, inert declaration, never permission to open its files.</summary>
public sealed class SharedSkillReferenceDeclaration
{
    public const string Format = "skill-md-instructions-v2";
    public const int MaximumReferences = 4;
    public const int MaximumReferenceBytes = 16 * 1024;
    public const int MaximumBundleBytes = 64 * 1024;

    private SharedSkillReferenceDeclaration(string[] paths) => Paths = Array.AsReadOnly(paths);

    public ReadOnlyCollection<string> Paths { get; }

    public static SharedSkillReferenceDeclaration Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var paths = value.Split(", ", StringSplitOptions.None);
        if (paths.Length > MaximumReferences
            || paths.Distinct(StringComparer.OrdinalIgnoreCase).Count() != paths.Length
            || paths.Any(path => !IsPath(path)))
        { throw new InvalidDataException("Declare one to four distinct canonical package-relative Markdown paths."); }
        return new(paths);
    }

    public bool MatchesInventory(string primary, IReadOnlyList<string> entries)
    {
        var split = primary.LastIndexOf('\\');
        var prefix = split < 0 ? string.Empty : primary[..(split + 1)];
        var expected = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in Paths)
        {
            expected.Add(prefix + path);
            for (var index = path.IndexOf('\\'); index >= 0; index = path.IndexOf('\\', index + 1))
            { expected.Add(prefix + path[..index]); }
        }
        return expected.SetEquals(entries) && entries.Count == expected.Count;
    }

    private static bool IsPath(string path)
    {
        if (path.Length is 0 or > 240 || !path.EndsWith(".md", StringComparison.Ordinal)) { return false; }
        var parts = path[..^3].Split('\\');
        return parts.Length <= SharedSkillCatalogue.MaximumDepth
            && parts.All(part => part.Length is > 0 and <= 64
                && part.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_')
                && part is not ("con" or "prn" or "aux" or "nul")
                && !(part.Length == 4 && (part.StartsWith("com", StringComparison.Ordinal)
                    || part.StartsWith("lpt", StringComparison.Ordinal)) && char.IsAsciiDigit(part[3])))
            && !string.Equals(parts[^1], "skill", StringComparison.Ordinal);
    }
}
