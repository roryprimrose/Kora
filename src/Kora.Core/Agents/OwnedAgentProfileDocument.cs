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
}
