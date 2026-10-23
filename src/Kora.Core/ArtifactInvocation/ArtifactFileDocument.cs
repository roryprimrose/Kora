using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace Kora.Core.Artifacts;

public sealed partial class ArtifactFileDocument
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly byte[] bytes;

    private ArtifactFileDocument(byte[] bytes, string text, bool bom,
        Dictionary<string, string> metadata, ArtifactDefinition definition)
    {
        this.bytes = bytes;
        Text = text;
        HasBom = bom;
        Metadata = new ReadOnlyDictionary<string, string>(metadata);
        Definition = definition;
    }

    public string Text { get; }
    public bool HasBom { get; }
    public int ByteCount => bytes.Length;
    public IReadOnlyDictionary<string, string> Metadata { get; }
    public ArtifactDefinition Definition { get; }
    public byte[] CopyBytes() => bytes.ToArray();

    public static ArtifactFileDocument PrepareOwnedSave(ArtifactKind kind, string name,
        OwnedInstructionSnapshot? original, string draft)
    {
        _ = OwnedDefinitionKind.DirectoryName(kind);
        OwnedInstructionNames.WriteName(name);
        if (original is not null)
        {
            original.Document.RequireEditableInstruction();
            if (original.Document.Definition.Kind != kind
                || !string.Equals(original.Name, name, StringComparison.Ordinal)
                || !string.Equals(original.CanonicalFileName, name + OwnedDefinitionKind.Extension(kind), StringComparison.Ordinal))
            { throw new InvalidDataException("The saved baseline must be the exact canonical owned ID and kind."); }
        }
        var proposed = Parse(Encode(draft, original?.Document.HasBom ?? false), kind, "kora-profile", name);
        proposed.RequireEditableInstruction();
        return original is null || !string.Equals(original.Document.Text, draft, StringComparison.Ordinal)
            ? proposed.WithNativeInstructionVersion(name) : proposed;
    }

    public ArtifactFileDocument WithOwnedCopyName(string name)
    {
        RequireOwnedCopySource();
        OwnedInstructionNames.WriteName(name);
        var offset = 3;
        if (Text[offset] == '\r') { offset++; }
        if (Text[offset] == '\n') { offset++; }
        using var reader = new StringReader(Text);
        reader.ReadLine();
        while (true)
        {
            var line = reader.ReadLine()!;
            if (string.Equals(line, "---", StringComparison.Ordinal)) { break; }
            var separator = line.IndexOf(':');
            if (string.Equals(line[..separator].Trim(), "name", StringComparison.Ordinal))
            {
                var start = separator + 1;
                var end = line.Length;
                while (char.IsWhiteSpace(line[start])) { start++; }
                while (char.IsWhiteSpace(line[end - 1])) { end--; }
                if (end - start >= 2 && line[start] == line[end - 1] && line[start] is '\'' or '"')
                { start++; end--; }
                return Parse(Encode(Text[..(offset + start)] + name + Text[(offset + end)..], HasBom),
                    Definition.Kind, "kora-profile", name);
            }
            offset += line.Length;
            if (Text[offset] == '\r') { offset++; }
            if (Text[offset] == '\n') { offset++; }
        }
        // An instruction without a declared name derives it from the new canonical filename.
        return Parse(bytes, Definition.Kind, "kora-profile", name);
    }

    public void RequireOwnedCopySource()
    {
        RequireEditableInstruction();
        if (!Metadata.ContainsKey("kora-format"))
        { throw new InvalidDataException("Save as new copy requires an existing supported native owned source; legacy files are not implicitly upgraded."); }
    }

    public ArtifactFileDocument WithNativeInstructionVersion(string name)
    {
        RequireEditableInstruction();
        if (Metadata.ContainsKey("kora-format")) { return this; }
        var newline = Text.AsSpan(3).StartsWith("\r\n", StringComparison.Ordinal) ? "\r\n"
            : Text[3] == '\r' ? "\r" : "\n";
        var offset = 3 + newline.Length;
        var upgraded = Text[..offset] + "kora-format: 1" + newline + Text[offset..];
        var document = Parse(Encode(upgraded, HasBom), Definition.Kind, "kora-profile", name);
        document.RequireEditableInstruction();
        return document;
    }

    public static ArtifactFileDocument Parse(ReadOnlySpan<byte> original, ArtifactKind kind,
        string source, string fileCommand) => ParseCore(original, kind, source, fileCommand, invocableOnly: false)!;

    public static ArtifactFileDocument? ParseInvocable(ReadOnlySpan<byte> original, ArtifactKind kind,
        string source, string fileCommand) => ParseCore(original, kind, source, fileCommand, invocableOnly: true);

    private static ArtifactFileDocument? ParseCore(ReadOnlySpan<byte> original, ArtifactKind kind,
        string source, string fileCommand, bool invocableOnly)
    {
        if (original.Length is 0 or > ArtifactDefinition.MaximumContentLength)
        { throw new InvalidDataException("The definition must contain between 1 and 65536 bytes."); }
        var bom = original.StartsWith(Encoding.UTF8.Preamble);
        string text;
        try { text = Utf8.GetString(bom ? original[Encoding.UTF8.Preamble.Length..] : original); }
        catch (DecoderFallbackException exception)
        { throw new InvalidDataException("The definition is not strict UTF-8.", exception); }
        using var reader = new StringReader(text);
        if (!string.Equals(reader.ReadLine(), "---", StringComparison.Ordinal))
        { throw new InvalidDataException("The definition requires scalar frontmatter."); }
        var bodyOffset = NextLine(text, 0);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        var closed = false;
        while (reader.ReadLine() is { } line)
        {
            bodyOffset = NextLine(text, bodyOffset);
            if (string.Equals(line, "---", StringComparison.Ordinal)) { closed = true; break; }
            var separator = line.IndexOf(':');
            if (separator <= 0) { throw new InvalidDataException("Unsupported definition frontmatter."); }
            var key = line[..separator].Trim();
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && value[0] == value[^1] && value[0] is '\'' or '"')
            { value = value[1..^1]; }
            if (!metadata.TryAdd(key, value))
            { throw new InvalidDataException("Duplicate definition frontmatter."); }
        }
        if (!closed) { throw new InvalidDataException("Unterminated definition frontmatter."); }
        var body = new List<string>();
        while (reader.ReadLine() is { } line) { body.Add(line); }
        var nativeParameters = kind == ArtifactKind.Prompt && string.Equals(source, "kora-profile", StringComparison.Ordinal)
            && string.Equals(metadata.GetValueOrDefault("kora-format"), "2", StringComparison.Ordinal);
        var content = nativeParameters ? text[bodyOffset..] : string.Join(Environment.NewLine, body).Trim();
        if (string.IsNullOrWhiteSpace(content)) { throw new InvalidDataException("The definition requires instruction content."); }
        if (invocableOnly && kind == ArtifactKind.Skill
            && string.Equals(metadata.GetValueOrDefault("user-invocable"), "false", StringComparison.OrdinalIgnoreCase))
        { return null; }
        var declaredName = metadata.GetValueOrDefault("name");
        var command = Slug(declaredName ?? fileCommand);
        var name = declaredName ?? command;
        if (kind == ArtifactKind.Skill && !string.Equals(command, fileCommand, StringComparison.OrdinalIgnoreCase))
        { throw new InvalidDataException("A skill name must match its folder."); }
        var description = metadata.GetValueOrDefault("description")
            ?? (kind == ArtifactKind.Prompt ? $"Run the {name} prompt."
                : throw new InvalidDataException("The definition requires a description."));
        var digest = Convert.ToHexStringLower(SHA256.HashData(original));
        try
        {
            var definition = new ArtifactDefinition($"disk.{source}.{kind.ToString().ToLowerInvariant()}.{command}",
                kind, name, description, command,
                string.Equals(name, command, StringComparison.OrdinalIgnoreCase) ? [name] : [name, command],
                source, $"disk-{digest[..12]}", digest, content)
            {
                OwnedInstructionName = kind is ArtifactKind.Instruction or ArtifactKind.Prompt && string.Equals(source, "kora-profile", StringComparison.Ordinal) ? fileCommand : null,
                RequiresInstructionReview = kind is ArtifactKind.Instruction or ArtifactKind.Prompt && metadata.ContainsKey("kora-format"),
                PromptTemplate = nativeParameters ? OwnedPromptTemplate.Parse(content) : null,
            };
            var document = new ArtifactFileDocument(original.ToArray(), text, bom, metadata, definition);
            if (nativeParameters)
            {
                document.RequireEditableInstruction();
            }
            return document;
        }
        catch (ArgumentException exception)
        { throw new InvalidDataException("The definition metadata or content is invalid.", exception); }
    }

    public void RequireEditableInstruction()
    {
        _ = OwnedDefinitionKind.DirectoryName(Definition.Kind);
        if (!string.Equals(Definition.Source, "kora-profile", StringComparison.Ordinal))
        { throw new InvalidDataException("Only Kora-owned instructions and prompts can be edited."); }
        RequireSupportedEditorFormat();
    }

    private void RequireSupportedEditorFormat()
    {
        using var reader = new StringReader(Text);
        var headerBytes = 0;
        var count = 0;
        reader.ReadLine();
        string line;
        while (!string.Equals(line = reader.ReadLine()!, "---", StringComparison.Ordinal))
        {
            // Parse already established a closing header; this source string is immutable.
            headerBytes += Utf8.GetByteCount(line) + 2;
            count++;
            if (headerBytes > 4096 || count > 16 || line.Contains('\\')
                || line.Any(character => character is '\0' or '\t'))
            { throw new InvalidDataException("The original header cannot round-trip the supported editor format. External editing is required; fork is not implemented."); }
        }
        if (Metadata.Keys.Any(key => key is not ("name" or "description" or "kora-format" or "attribution"))
            || Metadata.TryGetValue("kora-format", out var version)
                && !(string.Equals(version, "1", StringComparison.Ordinal)
                    || string.Equals(version, "2", StringComparison.Ordinal) && Definition.Kind == ArtifactKind.Prompt))
        { throw new InvalidDataException("Unsupported metadata or reader version. External editing is required; fork is not implemented."); }
        if (Definition.Kind == ArtifactKind.Prompt
            && (!Metadata.ContainsKey("name") || !Metadata.ContainsKey("description")
                || !string.Equals(Metadata.GetValueOrDefault("kora-format"), "2", StringComparison.Ordinal)
                    && Definition.Content.Contains("${", StringComparison.Ordinal)))
        { throw new InvalidDataException("Owned prompt editing supports named, described literal saved requests only; parameter or substitution syntax is incompatible."); }
    }

    public static byte[] Encode(string text, bool bom)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            var encoded = Utf8.GetBytes(text);
            return bom ? [.. Encoding.UTF8.Preamble, .. encoded] : encoded;
        }
        catch (EncoderFallbackException exception)
        { throw new InvalidDataException("The draft is not valid Unicode.", exception); }
    }

    private static string Slug(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value.Trim().ToLowerInvariant())
        {
            if (character is >= 'a' and <= 'z' or >= '0' and <= '9') { builder.Append(character); }
            else if (builder.Length > 0 && builder[^1] != '-') { builder.Append('-'); }
        }
        var result = builder.ToString().Trim('-');
        if (string.IsNullOrWhiteSpace(result) || result.Length > 64)
        { throw new InvalidDataException("The name must produce a bounded ASCII command."); }
        return result;
    }

    private static int NextLine(string text, int offset)
    {
        while (offset < text.Length && text[offset] is not ('\r' or '\n')) { offset++; }
        if (offset < text.Length && text[offset++] == '\r' && offset < text.Length && text[offset] == '\n') { offset++; }
        return offset;
    }
}
