using System.Text.Json;
using System.Text.Json.Serialization;

using Kora.Core.Conversation;
using Kora.Core.Tools;

namespace Kora.Core.Dependencies;

public static partial class ModelToolProtocol
{
    private static readonly JsonSerializerOptions RetainedResultOptions = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        MaxDepth = 16,
        Converters = { new JsonStringEnumConverter() },
    };

    public static void ValidateRetainedReadOnlyResult(string capabilityId, string inputJson, string resultJson)
    {
        var descriptor = ReadOnlyCapabilityCatalog.FindExact(capabilityId);
        if (descriptor is null || CapabilityInputPolicy.Read(descriptor, inputJson).DenialReason is not null)
        { throw new InvalidDataException("Retained observation input is not a supported bounded local request."); }
        using var document = ReadJson(StrictUtf8.GetBytes(resultJson), ReadOnlyCapabilityCatalog.Limits.MaximumOutputUtf8Bytes);
        CapabilityReply reply;
        try
        {
            reply = JsonSerializer.Deserialize<CapabilityReply>(resultJson, RetainedResultOptions)
                ?? throw new InvalidDataException("A retained observation cannot be null.");
            ScreenStrings(document.RootElement);
        }
        catch (JsonException exception)
        { throw new InvalidDataException("Retained observation shape is malformed.", exception); }
        if (reply.Outcome != CapabilityOutcome.Succeeded || string.IsNullOrWhiteSpace(reply.Reason)
            || !StrictUtf8.GetBytes(resultJson).AsSpan().SequenceEqual(CapabilityReplyWire.Serialize(reply)))
        { throw new InvalidDataException("Retained observation bytes are not an exact successful host result."); }
        ValidateResult(new("host-observation", capabilityId, inputJson), reply, historical: true);
    }

    private static void ScreenStrings(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            var text = value.GetString()!;
            if (ConversationText.Measure(text).HasUnsupportedContent)
            { throw new InvalidDataException("Retained observation text is malformed."); }
            ConversationText.RequireNoCredentials(text);
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in value.EnumerateObject()) { ScreenStrings(property.Value); }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray()) { ScreenStrings(item); }
        }
    }

    private static bool ValidHistoricalDescriptor(CapabilityDescriptor item) =>
        item is not null && ReadOnlyCapabilityCatalog.FindExact(item.Id) is not null && item.SchemaVersion > 0
        && Enum.IsDefined(item.Input) && Enum.IsDefined(item.Output) && Enum.IsDefined(item.Effect)
        && Enum.IsDefined(item.Availability) && item.CallerLanes is { Count: > 0 } lanes
        && lanes.All(Enum.IsDefined) && item.Limits is { MaximumInputUtf8Bytes: > 0, MaximumRecords: > 0, MaximumOutputUtf8Bytes: > 0 };
}
