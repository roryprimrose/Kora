namespace Kora.Core.Artifacts;

/// <summary>An immutable, explicitly registered prompt resource, not an owned file or execution authority.</summary>
public sealed class BundledPromptSource
{
    private readonly byte[] bytes;

    public BundledPromptSource(string id, string resourceId, ReadOnlySpan<byte> original)
    {
        if (string.IsNullOrWhiteSpace(resourceId) || !resourceId.StartsWith("Kora.Prompts.", StringComparison.Ordinal)
            || resourceId.Length > 128 || resourceId.Any(character =>
                character is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.')))
        { throw new InvalidDataException("A bundled prompt requires an explicit bounded resource identity."); }
        var document = ArtifactFileDocument.Parse(original, ArtifactKind.Prompt, "bundled", "prompt");
        if (document.Metadata.GetValueOrDefault("kora-format") is not "2"
            || !document.Metadata.ContainsKey("name") || !document.Metadata.ContainsKey("description")
            || document.Metadata.Keys.Any(key => key is not ("name" or "description" or "kora-format")))
        { throw new InvalidDataException("Bundled prompts require the exact named, described literal v2 format."); }
        var parsed = document.Definition;
        Definition = new(id, ArtifactKind.Prompt, parsed.Name, parsed.Description, parsed.CommandName,
            parsed.SpokenNames, "bundled", "1", parsed.Digest, document.Body)
        {
            RequiresInstructionReview = true,
            PromptTemplate = OwnedPromptTemplate.Parse(document.Body),
        };
        ResourceId = resourceId;
        SourceText = document.Text;
        bytes = document.CopyBytes();
    }

    public string ResourceId { get; }
    public string SourceText { get; }
    public ArtifactDefinition Definition { get; }
    public byte[] CopyBytes() => bytes.ToArray();

    public bool Matches(ArtifactDefinition candidate) =>
        candidate.Kind == ArtifactKind.Prompt && candidate.Source is "bundled"
        && string.Equals(candidate.Id, Definition.Id, StringComparison.Ordinal)
        && string.Equals(candidate.Digest, Definition.Digest, StringComparison.Ordinal)
        && string.Equals(candidate.Name, Definition.Name, StringComparison.Ordinal)
        && string.Equals(candidate.Description, Definition.Description, StringComparison.Ordinal)
        && string.Equals(candidate.CommandName, Definition.CommandName, StringComparison.Ordinal)
        && string.Equals(candidate.Version, Definition.Version, StringComparison.Ordinal)
        && string.Equals(candidate.Content, Definition.Content, StringComparison.Ordinal)
        && candidate.SpokenNames.SequenceEqual(Definition.SpokenNames, StringComparer.Ordinal)
        && candidate.OwnedInstructionName is null && candidate.OwnedInstructionRevision is null
        && candidate.UseControlId is null && candidate.RequiresInstructionReview
        && ReferenceEquals(candidate.PromptTemplate, Definition.PromptTemplate);
}
