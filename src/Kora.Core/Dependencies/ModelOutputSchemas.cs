using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Kora.Core.Agents;

namespace Kora.Core.Dependencies;

public static class ModelOutputSchemas
{
    private static readonly JsonSerializerOptions ProposalJson = new()
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    private static readonly JsonElement BodySchema = JsonSerializer.SerializeToElement(
        ProposalJson.GetJsonSchemaAsNode(typeof(BodyContent), new() { TreatNullObliviousAsNonNullable = true }));
    private static readonly JsonElement PromptSchema = JsonSerializer.SerializeToElement(new
    {
        oneOf = new[]
        {
            BodySchema,
            JsonSerializer.SerializeToElement(ProposalJson.GetJsonSchemaAsNode(typeof(UnsupportedContent),
                new() { TreatNullObliviousAsNonNullable = true })),
        },
    });

    public static JsonElement? GetSchema(ModelOutputFormat format) => format switch
    {
        ModelOutputFormat.Text => null,
        ModelOutputFormat.OwnedAgentProposal => OwnedAgentProfileDocument.ProposalSchema,
        ModelOutputFormat.DefinitionBodyProposal => BodySchema,
        ModelOutputFormat.PromptTemplateProposal => PromptSchema,
        _ => throw new InvalidDataException("Unknown host-selected model output format. No fallback is admitted."),
    };

    private sealed record BodyContent([property: JsonPropertyName("body")] string Body);
    private sealed record UnsupportedContent([property: JsonPropertyName("unsupported")] string Unsupported);
}
