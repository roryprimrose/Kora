using System.Text.Json;
using System.Text.Json.Serialization;

namespace Kora.Core.Tools;

/// <summary>The registry wire envelope is the authoritative serialized result bound.</summary>
public static class CapabilityReplyWire
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    public static byte[] Serialize(CapabilityReply reply) => JsonSerializer.SerializeToUtf8Bytes(reply, Options);
    public static int GetSerializedSize(CapabilityReply reply) => Serialize(reply).Length;
}
