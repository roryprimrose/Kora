using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace Kora.Core.Skills;

public sealed class SharedSkillSnapshot
{
    public const string ReaderVersion = "skill-md-instructions-v1";
    public const int MaximumBytes = 64 * 1024;
    public const string InvocationUnavailableReason =
        "Inspection alone grants no invocation authority. Compatible single-file, zero-tool instructions require separate volatile review, exact-revision enablement and exact-conversation confirmation. Registration grants no execution, egress, approval or trust promotion.";
    private readonly byte[] bytes;

    private SharedSkillSnapshot(Guid sourceId, string relativeFile, byte[] content, string? text,
        string? name, string? version, string? description, IEnumerable<string> tools, IEnumerable<string> reasons,
        IEnumerable<string>? uninspectedFiles = null, SharedSkillReferenceDeclaration? declaration = null,
        SharedSkillReferenceBundle? bundle = null, string? packageDirectoryIdentity = null,
        DeclarativeMcpToolReference? mcpReference = null)
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
        ReferenceDeclaration = declaration;
        ReferenceBundle = bundle;
        PackageDirectoryIdentity = packageDirectoryIdentity;
        McpReference = mcpReference;
        RevisionDigest = bundle?.RevisionDigest ?? Convert.ToHexStringLower(SHA256.HashData(bytes));
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
    public SharedSkillReferenceDeclaration? ReferenceDeclaration { get; }
    public SharedSkillReferenceBundle? ReferenceBundle { get; }
    public string? PackageDirectoryIdentity { get; }
    public DeclarativeMcpToolReference? McpReference { get; }
    public bool IsReferenceCandidate => ReferenceDeclaration is not null && ReferenceBundle is null
        && UnavailableReasons.SequenceEqual(["reference-read-consent-required"], StringComparer.Ordinal);
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
        var fields = SharedSkillFrontMatter.Parse(text, reasons);
        SharedSkillReferenceDeclaration? declaration = null;
        fields.TryGetValue("format", out var format);
        DeclarativeMcpToolReference? mcpReference = null;
        if (format is DeclarativeMcpToolReference.Format)
        {
            if (fields.Count != 8 || !fields.TryGetValue("allowed-tools", out var tool)
                || !tool.StartsWith("mcp.", StringComparison.Ordinal)
                || !fields.TryGetValue("mcp-server", out var server)
                || !fields.TryGetValue("mcp-source-revision", out var revision)
                || !fields.TryGetValue("mcp-schema-digest", out var schema))
            { reasons.Add("invalid-mcp-reference"); }
            else
            {
                try { mcpReference = new(server, revision, tool["mcp.".Length..], schema); }
                catch (InvalidDataException) { reasons.Add("invalid-mcp-reference"); }
            }
        }
        else if (format is not null || fields.ContainsKey("references"))
        {
            if (!string.Equals(format, SharedSkillReferenceDeclaration.Format, StringComparison.Ordinal) || !fields.TryGetValue("references", out var references))
            { reasons.Add("unsupported-reference-format"); }
            else
            {
                try { declaration = SharedSkillReferenceDeclaration.Parse(references); }
                catch (InvalidDataException) { reasons.Add("invalid-reference-declaration"); }
            }
        }
        if (format is not DeclarativeMcpToolReference.Format
            && fields.Keys.Any(key => key.StartsWith("mcp-", StringComparison.Ordinal)))
        { reasons.Add("unsupported-mcp-reference-format"); }
        if (declaration is not null)
        {
            if (!declaration.MatchesInventory(relativeFile, uninspectedFiles ?? []))
            { reasons.Add("undeclared-or-missing-package-entries"); }
            reasons.Add("reference-read-consent-required");
        }
        else if (containsOtherFiles || uninspectedFiles is { Count: > 0 })
        { reasons.Add("additional-package-files-not-supported"); }
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
                || tools.Any(tool => tool.Length > 80 || mcpReference is null && tool.Any(c =>
                    c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-' or '_'))))
            { reasons.Add("invalid-declared-tools"); }
            // No runtime tool/task dependency admission is provided by this inspection reader.
            reasons.Add("declared-tools-unavailable");
        }
        ValidateInstructionText(text, declaration, reasons);
        return new(sourceId, relativeFile, copy, text, name, version, description, tools, reasons, uninspectedFiles,
            declaration, mcpReference: mcpReference);
    }

    internal static void ValidateInstructionText(string text, SharedSkillReferenceDeclaration? declaration, List<string> reasons)
    {
        var projection = text.StartsWith('\uFEFF') ? text[1..] : text;
        if (projection.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t')
            || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.Format))
        { reasons.Add("invalid-control-text"); }
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
            {
                var language = trimmed[3..].Trim();
                if (language.Length > 0 && language is not ("text" or "markdown" or "md"))
                { reasons.Add("executable-or-unknown-code-fence"); }
            }
            var withoutLinks = trimmed;
            if (declaration is not null)
            {
                var start = withoutLinks.IndexOf('[', StringComparison.Ordinal);
                while (start >= 0)
                {
                    var labelEnd = withoutLinks.IndexOf("](", start, StringComparison.Ordinal);
                    if (labelEnd < 0 || start > 0 && withoutLinks[start - 1] == '!'
                        || withoutLinks[(start + 1)..labelEnd].Any(c => c is '[' or ']' or '!' or '<')) { break; }
                    var end = withoutLinks.IndexOf(')', labelEnd + 2);
                    if (end < 0 || !declaration.Paths.Contains(withoutLinks[(labelEnd + 2)..end], StringComparer.Ordinal)) { break; }
                    withoutLinks = withoutLinks.Remove(start, end + 1 - start);
                    start = withoutLinks.IndexOf('[', StringComparison.Ordinal);
                }
            }
            if (withoutLinks.Contains("](", StringComparison.Ordinal)
                || withoutLinks.Contains('[')
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
    }

    public SharedSkillSnapshot WithUnavailableReason(string reason) =>
        new(SourceId, RelativeFile, bytes, Text, Name, Version, Description, DeclaredTools, UnavailableReasons.Append(reason),
            UninspectedFiles, ReferenceDeclaration, ReferenceBundle, PackageDirectoryIdentity, McpReference);

    public SharedSkillSnapshot WithPackageDirectoryIdentity(string identity)
    {
        if (identity.Length != 48 || identity.Any(c => !char.IsAsciiHexDigit(c)))
        { throw new InvalidDataException("A pinned native package identity is required."); }
        return new(SourceId, RelativeFile, bytes, Text, Name, Version, Description, DeclaredTools, UnavailableReasons,
            UninspectedFiles, ReferenceDeclaration, ReferenceBundle, identity, McpReference);
    }

    public SharedSkillSnapshot WithReferenceBundle(SharedSkillReferenceBundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        if (!IsReferenceCandidate || PackageDirectoryIdentity is null)
        { throw new InvalidOperationException("Only an exact pinned reference candidate can accept a complete bundle."); }
        var verified = SharedSkillReferenceBundle.Create(this, bundle.References.ToDictionary(item => item.RelativePath, item => item.Bytes.ToArray(), StringComparer.Ordinal));
        if (!string.Equals(verified.RevisionDigest, bundle.RevisionDigest, StringComparison.Ordinal))
        { throw new InvalidDataException("The bundle belongs to a different primary revision."); }
        return new(SourceId, RelativeFile, bytes, Text, Name, Version, Description, DeclaredTools, [],
            UninspectedFiles, ReferenceDeclaration, bundle, PackageDirectoryIdentity);
    }

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
