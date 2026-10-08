using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace Kora.Core.Skills;

public sealed class SharedSkillSnapshot
{
    public const string ReaderVersion = "skill-md-instructions-v1";
    public const int MaximumBytes = 64 * 1024;
    public const string InvocationUnavailableReason =
        "Read-only local inspection. Enable, disable, invoke, model exposure and Kora-specific authoring are unavailable. Registration grants no execution, egress, approval or trust promotion.";
    private readonly byte[] bytes;

    private SharedSkillSnapshot(Guid sourceId, string relativeFile, byte[] content, string? text,
        string? name, string? version, string? description, IEnumerable<string> tools, IEnumerable<string> reasons,
        IEnumerable<string>? uninspectedFiles = null)
    {
        SourceId = sourceId;
        RelativeFile = relativeFile;
        bytes = content;
        Text = text;
        Name = name;
        Version = version;
        Description = description;
        DeclaredTools = tools.ToList().AsReadOnly();
        UninspectedFiles = (uninspectedFiles ?? []).ToList().AsReadOnly();
        UnavailableReasons = reasons.Distinct(StringComparer.Ordinal).ToList().AsReadOnly();
        RevisionDigest = Convert.ToHexStringLower(SHA256.HashData(bytes));
        SourceQualifiedIdentity = $"shared-profile:{sourceId:N}:{name ?? relativeFile}@{version ?? "unversioned"}";
    }

    public Guid SourceId { get; }
    public string RelativeFile { get; }
    public string SourceQualifiedIdentity { get; }
    public string RevisionDigest { get; }
    public string? Text { get; }
    public string? Name { get; }
    public string? Version { get; }
    public string? Description { get; }
    public ReadOnlySpan<byte> Bytes => bytes;
    public ReadOnlyCollection<string> DeclaredTools { get; }
    public ReadOnlyCollection<string> UninspectedFiles { get; }
    public ReadOnlyCollection<string> UnavailableReasons { get; }
    public bool IsInstructionCompatible => UnavailableReasons.Count == 0;
    public bool IsAvailableForInvocation => false;

    public static SharedSkillSnapshot Parse(Guid sourceId, string relativeFile, ReadOnlySpan<byte> content,
        bool containsOtherFiles, IReadOnlyList<string>? uninspectedFiles = null)
    {
        if (sourceId == Guid.Empty || content.Length is 0 or > MaximumBytes
            || relativeFile.Length is 0 or > 512 || relativeFile.Split('\\').Any(part =>
                part.Length == 0 || part is "." or ".." || part.Any(c => char.IsControl(c) || c is '/' or ':')))
        {
            throw new InvalidDataException("A bounded exact skill file and source identity are required.");
        }
        if (uninspectedFiles is not null && (uninspectedFiles.Count > SharedSkillCatalogue.MaximumEntries
            || uninspectedFiles.Any(file => file.Length is 0 or > 512
                || file.Split('\\').Any(part => part.Length == 0 || part is "." or ".."
                    || part.Any(c => char.IsControl(c) || c is '/' or ':')))))
        { throw new InvalidDataException("Additional entry disclosure must remain bounded within the selected source."); }
        var copy = content.ToArray();
        string text;
        try { text = new UTF8Encoding(false, true).GetString(copy); }
        catch (DecoderFallbackException)
        {
            return new(sourceId, relativeFile, copy, null, null, null, null, [], ["invalid-utf8"], uninspectedFiles);
        }
        var projection = text.StartsWith('\uFEFF') ? text[1..] : text;
        if (projection.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')
            || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format))
        {
            return new(sourceId, relativeFile, copy, text, null, null, null, [], ["invalid-control-text"], uninspectedFiles);
        }
        var reasons = new List<string>();
        if (containsOtherFiles || uninspectedFiles is { Count: > 0 }) { reasons.Add("additional-package-files-not-supported"); }
        var fields = SharedSkillFrontMatter.Parse(text, reasons);
        fields.TryGetValue("name", out var name);
        fields.TryGetValue("version", out var version);
        fields.TryGetValue("description", out var description);
        if (!IsName(name)) { reasons.Add("invalid-required-name"); name = null; }
        if (version is null || !IsVersion(version)) { reasons.Add("invalid-required-version"); version = null; }
        if (string.IsNullOrWhiteSpace(description) || description.Length > 1024)
        { reasons.Add("invalid-required-description"); }
        var tools = new List<string>();
        if (fields.TryGetValue("allowed-tools", out var declared))
        {
            tools.AddRange(declared.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries));
            if (tools.Count is 0 or > 16 || tools.Distinct(StringComparer.OrdinalIgnoreCase).Count() != tools.Count
                || tools.Any(tool => tool.Length > 80 || tool.Any(c =>
                    c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-'))))
            { reasons.Add("invalid-declared-tools"); }
            // No runtime tool/task dependency admission is provided by this inspection reader.
            reasons.Add("declared-tools-unavailable");
        }
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                var language = trimmed[3..].Trim();
                if (language.Length > 0 && language is not ("text" or "markdown" or "md"))
                { reasons.Add("executable-or-unknown-code-fence"); }
            }
            if (trimmed.Contains("](", StringComparison.Ordinal)
                || trimmed.Contains('[')
                || trimmed.Contains("http:", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains("https:", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains('<'))
            { reasons.Add("unresolved-reference-or-active-markup"); }
            if (trimmed.Contains(".ps1", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains(".sh", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains(".exe", StringComparison.OrdinalIgnoreCase)
                || trimmed.Contains(".py", StringComparison.OrdinalIgnoreCase))
            { reasons.Add("executable-workflow-reference"); }
        }
        return new(sourceId, relativeFile, copy, text, name, version, description, tools, reasons, uninspectedFiles);
    }

    public SharedSkillSnapshot WithUnavailableReason(string reason) =>
        new(SourceId, RelativeFile, bytes, Text, Name, Version, Description, DeclaredTools, UnavailableReasons.Append(reason), UninspectedFiles);

    private static bool IsName(string? name) => name is { Length: > 0 and <= 64 }
        && name[0] is >= 'a' and <= 'z' && name[^1] != '-'
        && !name.Contains("--", StringComparison.Ordinal)
        && name.All(c => c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-');

    private static bool IsVersion(string version)
    {
        var parts = version.Split('.');
        return parts.Length == 3 && parts.All(part => part.Length is > 0 and <= 4
            && part.All(char.IsAsciiDigit) && (part.Length == 1 || part[0] != '0'));
    }
}
