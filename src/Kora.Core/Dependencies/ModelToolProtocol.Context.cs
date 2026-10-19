using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;
using Kora.Core.Tools;

namespace Kora.Core.Dependencies;

public static partial class ModelToolProtocol
{
    private static readonly JsonSerializerOptions ContextOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        RespectRequiredConstructorParameters = true,
    };

    public static ModelToolContext ReadContext(ReadOnlySpan<byte> wire, ModelTurnProvenance provenance)
    {
        ValidateProvenance(provenance);
        using var document = ReadJson(wire, MaximumInputUtf8Bytes);
        RequireFields(document.RootElement, "SystemPolicy", "UserRequest", "Evidence", "Tools", "MaximumOutputUtf8Bytes");
        ContextContent content;
        try
        {
            content = document.RootElement.Deserialize<ContextContent>(ContextOptions)
                ?? throw new InvalidDataException("The context is missing.");
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        { throw new InvalidDataException("The host context has invalid typed fields.", exception); }
        if (content.SystemPolicy is null || content.UserRequest is null || content.Evidence is null
            || content.Tools is null || content.MaximumOutputUtf8Bytes != MaximumOutputUtf8Bytes
            || content.Tools.Length != ReadOnlyCapabilityCatalog.Descriptors.Count
            || content.Tools.Where((descriptor, index) => descriptor is null
                || !CapabilityReplyWire.IsExactDescriptor(descriptor, ReadOnlyCapabilityCatalog.Descriptors[index])).Any())
        { throw new InvalidDataException("The complete host context or catalogue does not match the admitted protocol."); }
        var evidence = ModelContextEnvelope.ValidateContent(provenance.Request, content.SystemPolicy,
            content.UserRequest, content.Evidence);
        return new(provenance, content.SystemPolicy, content.UserRequest, evidence, ImmutableArray.CreateRange(wire.ToArray()));
    }

    private sealed record ContextContent(string SystemPolicy, string UserRequest, ModelContextEvidence[] Evidence,
        CapabilityDescriptor[] Tools, int MaximumOutputUtf8Bytes);
}
