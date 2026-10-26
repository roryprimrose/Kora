using System.Text;
using System.Text.Json;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Windows.Dependencies;

/// <summary>Native Messages translation only; the portable protocol owns proposals and result validation.</summary>
internal static class ClaudeObservationWire
{
    internal static string Name(string id) => "kora_" + id.Replace('.', '_');

    internal static byte[] Serialize(ModelToolRequest request, int? outputLimit)
    {
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory))
        {
            writer.WriteStartObject();
            writer.WriteString("model", request.Profile!.Model.Value);
            writer.WriteString("system", request.Context.SystemPolicy);
            writer.WriteStartArray("messages");
            writer.WriteStartObject();
            writer.WriteString("role", "user");
            writer.WriteString("content", request.Context.UserRequest);
            writer.WriteEndObject();
            for (var index = 0; index < request.Messages.Length; index++)
            {
                var message = request.Messages[index];
                writer.WriteStartObject();
                writer.WriteString("role", message is ModelAssistantToolCalls ? "assistant" : "user");
                writer.WriteStartArray("content");
                if (message is ModelAssistantToolCalls assistant)
                {
                    foreach (var call in assistant.Calls)
                    {
                        writer.WriteStartObject();
                        writer.WriteString("type", "tool_use");
                        writer.WriteString("id", call.CallId);
                        writer.WriteString("name", Name(call.CapabilityId));
                        writer.WritePropertyName("input");
                        writer.WriteRawValue(call.InputJson);
                        writer.WriteEndObject();
                    }
                }
                else if (message is ModelToolResultMessage result)
                {
                    WriteResult(writer, result);
                    while (index + 1 < request.Messages.Length && request.Messages[index + 1] is ModelToolResultMessage next)
                    { WriteResult(writer, next); index++; }
                }
                else { throw new InvalidDataException("Only canonical observation history can enter Claude."); }
                writer.WriteEndArray();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartArray("tools");
            foreach (var descriptor in request.Context.Tools)
            {
                writer.WriteStartObject();
                writer.WriteString("name", Name(descriptor.Id));
                writer.WriteString("description", "Host-owned read-only observation: " + descriptor.Id);
                writer.WritePropertyName("input_schema");
                writer.WriteRawValue(ReadOnlyCapabilitySchema.Serialize(descriptor.Id));
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteStartObject("thinking");
            writer.WriteString("type", "disabled");
            writer.WriteEndObject();
            if (outputLimit is { } limit)
            {
                writer.WriteNumber("max_tokens", limit);
                writer.WriteBoolean("stream", false);
                if (request.Profile.Reasoning.Value is not "disabled")
                {
                    writer.WriteStartObject("output_config");
                    writer.WriteString("effort", request.Profile.Reasoning.Value);
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndObject();
        }
        if (memory.Length > ClaudeTransmission.MaximumBodyBytes)
        { throw new InvalidDataException("Complete Claude observation request exceeds its byte bound."); }
        return memory.ToArray();
    }

    private static void WriteResult(Utf8JsonWriter writer, ModelToolResultMessage result)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "tool_result");
        writer.WriteString("tool_use_id", result.CallId);
        writer.WriteString("content", Encoding.UTF8.GetString(result.Result.AsSpan()));
        writer.WriteEndObject();
    }

    internal static ModelAssistantMessage Parse(byte[] bytes, ProviderProfileTuple profile, int outputLimit,
        int inputLimit, IReadOnlyList<CapabilityDescriptor> tools)
    {
        using var json = ClaudeMessagesRuntime.Parse(bytes);
        var root = json.RootElement;
        if (root.GetProperty("stop_reason").GetString() is "end_turn")
        {
            return ModelToolProtocol.ParseAssistant(JsonSerializer.SerializeToUtf8Bytes(new
            { kind = "answer", answer = ClaudeMessagesRuntime.ParseAnswer(bytes, profile, outputLimit, inputLimit) }));
        }
        ClaudeMessagesRuntime.ValidateMessage(root, profile, outputLimit, inputLimit, "tool_use");
        var content = root.GetProperty("content");
        if (content.ValueKind != JsonValueKind.Array || content.GetArrayLength() is < 1 or > 6)
        { throw new InvalidDataException("Claude observation proposals exceed the finite call bound."); }
        using var memory = new MemoryStream();
        using (var writer = new Utf8JsonWriter(memory))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", "toolCalls");
            writer.WriteStartArray("calls");
            foreach (var block in content.EnumerateArray())
            {
                if (block.EnumerateObject().Count() != 4
                    || block.GetProperty("type").GetString() is not "tool_use")
                { throw new InvalidDataException("Only exact client tool proposals are admitted."); }
                var name = block.GetProperty("name").GetString();
                var descriptor = tools.SingleOrDefault(tool => string.Equals(Name(tool.Id), name, StringComparison.Ordinal))
                    ?? throw new InvalidDataException("Claude proposed a tool outside the advertised observation subset.");
                writer.WriteStartObject();
                writer.WriteString("callId", block.GetProperty("id").GetString());
                writer.WriteString("capabilityId", descriptor.Id);
                writer.WritePropertyName("input");
                writer.WriteRawValue(block.GetProperty("input").GetRawText());
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        return ModelToolProtocol.ParseAssistant(memory.ToArray());
    }
}
