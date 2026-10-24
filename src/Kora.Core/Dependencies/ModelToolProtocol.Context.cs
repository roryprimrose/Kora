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
        RequireFields(document.RootElement, "SystemPolicy", "UserRequest", "Evidence", "ToolMode", "Tools", "MaximumOutputUtf8Bytes");
        ContextContent content;
        try
        {
            // RequireFields has already established an object; the fixed serializer has no null-producing converter.
            content = document.RootElement.Deserialize<ContextContent>(ContextOptions)!;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException)
        { throw new InvalidDataException("The host context has invalid typed fields.", exception); }
        var tools = GetTools(content.ToolMode);
        if (content.SystemPolicy is null || content.UserRequest is null || content.Evidence is null
            || content.Tools is null || content.MaximumOutputUtf8Bytes != MaximumOutputUtf8Bytes
            || content.Tools.Length != tools.Count
            || content.Tools.Where((descriptor, index) => descriptor is null
                || !CapabilityReplyWire.IsExactDescriptor(descriptor, tools[index])).Any())
        { throw new InvalidDataException("The complete host context or catalogue does not match the admitted protocol."); }
        var evidence = ModelContextEnvelope.ValidateContent(provenance.Request, content.SystemPolicy,
            content.UserRequest, content.Evidence);
        return new(provenance, content.SystemPolicy, content.UserRequest, evidence, content.ToolMode,
            ImmutableArray.CreateRange(wire.ToArray()));
    }

    public static IReadOnlyList<CapabilityDescriptor> GetTools(ModelToolMode mode) => mode switch
    {
        ModelToolMode.ReadOnly => ReadOnlyCapabilityCatalog.Descriptors,
        ModelToolMode.None => [],
        ModelToolMode.LocalWebPage => Network.LocalWebPageTool.Descriptors,
        _ => throw new InvalidDataException("The complete turn requires a known host tool mode."),
    };

    private sealed record ContextContent(string SystemPolicy, string UserRequest, ModelContextEvidence[] Evidence,
        ModelToolMode ToolMode, CapabilityDescriptor[] Tools, int MaximumOutputUtf8Bytes);
}
