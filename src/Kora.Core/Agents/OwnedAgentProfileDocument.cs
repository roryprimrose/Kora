using Kora.Core.Artifacts;
using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Kora.Core.Agents;

/// <summary>A validated inert owned original, not execution or selection authority.</summary>
public sealed class OwnedAgentProfileDocument
{
    private static readonly JsonSerializerOptions ProposalJson = new()
    {
        AllowDuplicateProperties = false,
        MaxDepth = 2,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static JsonElement ProposalSchema { get; } = JsonSerializer.SerializeToElement(
        ProposalJson.GetJsonSchemaAsNode(typeof(ProposalContent), new() { TreatNullObliviousAsNonNullable = true }));

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
        { throw new InvalidDataException("Owned authoring supports reference-free v1 or owned-instruction/observation-tool v4 profiles."); }
        var profile = BoundedAgentProfile.Parse(document);
        if (!string.Equals(document.Metadata["name"], name, StringComparison.Ordinal))
        { throw new InvalidDataException("An owned profile requires its exact canonical ID as name."); }
        return new(name, profile);
    }

    public static OwnedAgentProfileDocument Parse(string name, string text) =>
        Parse(name, ArtifactFileDocument.Encode(text, false));

    public static OwnedAgentProfileDocument ParseProposal(string name, string source, string answer)
    {
        if (ArtifactFileDocument.Encode(answer, false).Length > ArtifactDefinition.MaximumContentLength)
        { throw new InvalidDataException("The complete agent proposal exceeds the source limit. Nothing was truncated or staged."); }
        try
        {
            var proposal = JsonSerializer.Deserialize<ProposalContent>(answer, ProposalJson)
                ?? throw new InvalidDataException("An agent proposal must contain only description and body.");
            return ProposeToolFree(name, source, proposal.Description, proposal.Body);
        }
        catch (JsonException exception)
        { throw new InvalidDataException("Invalid agent proposal JSON. No draft was changed.", exception); }
    }

    public static OwnedAgentProfileDocument ProposeToolFree(string name, string source, string description, string body)
    {
        if (string.IsNullOrWhiteSpace(description) || description.Length > ArtifactDefinition.MaximumDescriptionLength
            || description.Any(char.IsControl) || !string.Equals(description, description.Trim(), StringComparison.Ordinal)
            || description[0] is '\'' or '"' || description[^1] is '\'' or '"')
        { throw new InvalidDataException("A proposed description must be one bounded literal scalar, without quoting or control characters."); }
        var original = source.Length == 0 ? null : Parse(name, source);
        if (original is not null && (!string.Equals(original.Document.Metadata["kora-agent-format"], "1", StringComparison.Ordinal)
            || !original.Profile.Tools.IsEmpty))
        { throw new InvalidDataException("Conversational authoring supports only tool-free reference-free v1. Manual authoring is unchanged."); }
        var header = original is null
            ? $"---\nname: {name}\ndescription: {description}\nkora-agent-format: 1\nruntime: local\ninput: text\noutput: text\n"
                + "references: none\ntools: none\nmaximum-calls: 0\nmaximum-rounds: 1\n"
                + "maximum-input-bytes: 16000\nmaximum-output-bytes: 2000\ndeadline-ms: 15000\n"
            : "---\n" + string.Concat(original.Document.Metadata.Select(field =>
                field.Key + ": " + (string.Equals(field.Key, "description", StringComparison.Ordinal) ? description : field.Value) + "\n"));
        return Parse(name, header + "---\n" + body);
    }

    private sealed record ProposalContent(
        [property: JsonPropertyName("description")] string Description,
        [property: JsonPropertyName("body")] string Body);
}
