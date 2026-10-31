using Kora.Core.Artifacts;

namespace Kora.Core.Agents;

/// <summary>A validated inert owned original, not execution or selection authority.</summary>
public sealed class OwnedAgentProfileDocument
{
    private OwnedAgentProfileDocument(string name, BoundedAgentProfile profile)
    { Name = name; Profile = profile; }

    public string Name { get; }
    public BoundedAgentProfile Profile { get; }
    public ArtifactFileDocument Document => Profile.Document;

    public static OwnedAgentProfileDocument Parse(string name, ReadOnlySpan<byte> bytes)
    {
        OwnedInstructionNames.WriteName(name);
        var document = ArtifactFileDocument.Parse(bytes, ArtifactKind.Agent, "kora-profile", name);
        if (!document.Metadata.TryGetValue("kora-agent-format", out var format)
            || format is not ("1" or "4"))
        { throw new InvalidDataException("Owned authoring supports tool-free reference-free v1 or owned-instruction/observation-tool v4 profiles."); }
        var profile = BoundedAgentProfile.Parse(document);
        if (format is "1" && profile.Tools.Length != 0
            || !string.Equals(document.Metadata["name"], name, StringComparison.Ordinal))
        { throw new InvalidDataException("A new owned profile requires its exact canonical ID as name; v1 requires tools none and zero calls."); }
        return new(name, profile);
    }

    public static OwnedAgentProfileDocument Parse(string name, string text) =>
        Parse(name, ArtifactFileDocument.Encode(text, false));

    public static OwnedAgentProfileDocument ProposeToolFree(string name, string source, string description, string body)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Length > ArtifactDefinition.MaximumDescriptionLength
            || description.Any(char.IsControl) || !string.Equals(description, description.Trim(), StringComparison.Ordinal)
            || description[0] is '\'' or '"' || description[^1] is '\'' or '"')
        { throw new InvalidDataException("A proposed description must be one bounded literal scalar, without quoting or control characters."); }
        var original = source.Length == 0 ? null : Parse(name, source);
        if (original is not null && !string.Equals(original.Document.Metadata["kora-agent-format"], "1", StringComparison.Ordinal))
        { throw new InvalidDataException("Conversational authoring supports only tool-free reference-free v1. Manual v4 authoring is unchanged."); }
        var header = original is null
            ? $"---\nname: {name}\ndescription: {description}\nkora-agent-format: 1\nruntime: local\ninput: text\noutput: text\n"
                + "references: none\ntools: none\nmaximum-calls: 0\nmaximum-rounds: 1\n"
                + "maximum-input-bytes: 16000\nmaximum-output-bytes: 2000\ndeadline-ms: 15000\n"
            : "---\n" + string.Concat(original.Document.Metadata.Select(field =>
                field.Key + ": " + (string.Equals(field.Key, "description", StringComparison.Ordinal) ? description : field.Value) + "\n"));
        return Parse(name, header + "---\n" + body);
    }
}
