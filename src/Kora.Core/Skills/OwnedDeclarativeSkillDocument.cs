using System.Globalization;
using System.Text;

namespace Kora.Core.Skills;

/// <summary>The closed native v1 two-file format. Guidance is data, never executable metadata.</summary>
public sealed class OwnedDeclarativeSkillDocument
{
    private static readonly UTF8Encoding Encoding = new(false, true);
    private readonly byte[] manifest;
    private readonly byte[] instructions;

    private OwnedDeclarativeSkillDocument(string id, string version, string name, string description,
        byte[] manifest, byte[] instructions)
    {
        Id = id;
        Version = version;
        Name = name;
        Description = description;
        this.manifest = manifest;
        this.instructions = instructions;
    }

    public string Id { get; }
    public string Version { get; }
    public string Name { get; }
    public string Description { get; }
    public string ManifestText => Encoding.GetString(manifest);
    public string Guidance => Encoding.GetString(instructions);
    public ReadOnlyMemory<byte> ManifestBytes => manifest;
    public ReadOnlyMemory<byte> InstructionBytes => instructions;
    public bool IsAvailableForInvocation => false;

    public static void RequireIdentity(string id, string version)
    {
        if (id.Length is 0 or > 64 || id[0] is not (>= 'a' and <= 'z')
            || id[^1] == '-' || id.Contains("--", StringComparison.Ordinal)
            || id.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '-'))
            || id is "con" or "prn" or "aux" or "nul"
            || id.Length == 4 && (id.StartsWith("com", StringComparison.Ordinal)
                || id.StartsWith("lpt", StringComparison.Ordinal)) && id[3] is >= '0' and <= '9')
        { throw new InvalidDataException("Use a canonical lowercase skill ID, not a path, device or alias."); }
        var parts = version.Split('.');
        if (parts.Length != 3 || parts.Any(p => p.Length is 0 or > 4
            || p.Any(c => !char.IsAsciiDigit(c)) || p.Length > 1 && p[0] == '0'))
        { throw new InvalidDataException("Use a canonical three-part version such as 1.0.0."); }
    }

    public static OwnedDeclarativeSkillDocument Create(string id, string version, string name,
        string description, string guidance)
    {
        RequireIdentity(id, version);
        RequireScalar(name, 128);
        RequireScalar(description, 1024);
        var text = $"kora-skill-format: 1\nid: {id}\nversion: {version}\nname: {name}\ndescription: {description}\nruntime: local\ntools: none\nreferences: none\n";
        try { return Parse(Encoding.GetBytes(text), Encoding.GetBytes(guidance)); }
        catch (EncoderFallbackException exception)
        { throw new InvalidDataException("Skill text must be well-formed Unicode.", exception); }
    }

    public static OwnedDeclarativeSkillDocument Parse(ReadOnlySpan<byte> manifest, ReadOnlySpan<byte> instructions)
    {
        if (manifest.Length is 0 or > 4096 || instructions.Length == 0
            || manifest.Length + instructions.Length > SharedSkillSnapshot.MaximumBytes)
        { throw new InvalidDataException("The complete two-file skill must fit within 64 KiB; metadata within 4096 bytes."); }
        string metadata;
        string guidance;
        try { metadata = Encoding.GetString(manifest); guidance = Encoding.GetString(instructions); }
        catch (DecoderFallbackException exception)
        { throw new InvalidDataException("Skill files require strict UTF-8.", exception); }
        RequireText(metadata);
        RequireText(guidance);
        if (string.IsNullOrWhiteSpace(guidance))
        { throw new InvalidDataException("Literal skill guidance is required."); }
        var lines = metadata.Split('\n');
        if (lines.Length != 9 || lines[^1].Length != 0)
        { throw new InvalidDataException("The native manifest requires exactly eight scalar fields and a final newline."); }
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in lines[..^1])
        {
            var split = line.IndexOf(": ", StringComparison.Ordinal);
            if (split < 1 || !fields.TryAdd(line[..split], line[(split + 2)..]))
            { throw new InvalidDataException("Malformed or duplicate native skill metadata."); }
        }
        string Field(string key) => fields.TryGetValue(key, out var value) ? value
            : throw new InvalidDataException("Unknown or missing native skill metadata.");
        if (Field("kora-skill-format") is not "1" || Field("runtime") is not "local"
            || Field("tools") is not "none" || Field("references") is not "none")
        { throw new InvalidDataException("Only native v1 local, tool-free, reference-free skills can be saved."); }
        var id = Field("id");
        var version = Field("version");
        RequireIdentity(id, version);
        var name = Field("name");
        var description = Field("description");
        RequireScalar(name, 128);
        RequireScalar(description, 1024);
        return new(id, version, name, description, manifest.ToArray(), instructions.ToArray());
    }

    private static void RequireText(string text)
    {
        if (text.Any(c => char.IsControl(c) && c is not ('\n' or '\r' or '\t')
            || char.GetUnicodeCategory(c) == UnicodeCategory.Format))
        { throw new InvalidDataException("Control/format characters and BOMs are not supported in native skill files."); }
    }

    private static void RequireScalar(string value, int maximum)
    {
        RequireText(value);
        if (string.IsNullOrWhiteSpace(value) || value.Length > maximum || !string.Equals(value, value.Trim(), StringComparison.Ordinal)
            || value.Any(c => char.IsControl(c) || c is ':' or '"' or '\'' or '\\' or '&' or '*' or '!'
                or '{' or '}' or '[' or ']' or '|' or '>' or '#' or '`')
            || value[0] is '-' or '?')
        { throw new InvalidDataException("Name and description require bounded plain one-line scalar text."); }
    }
}
