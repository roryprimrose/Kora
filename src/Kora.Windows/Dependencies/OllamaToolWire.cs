using System.Globalization;
using System.Text;
using System.Text.Json;
using Kora.Core.Dependencies;
using Kora.Core.Tools;

namespace Kora.Windows.Dependencies;

/// <summary>Native api/chat framing only. Capability admission and canonical messages remain Core-owned.</summary>
internal static partial class OllamaToolWire
{
    internal sealed record Exchange(ModelAssistantToolCalls Calls, byte[] NativeMessage);
    internal sealed record Reply(ModelAssistantMessage Message, byte[] NativeMessage);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static byte[] WriteRequest(ModelToolRequest request, string model, IReadOnlyList<Exchange> exchanges, bool streaming = false)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("model", model);
            writer.WriteBoolean("stream", streaming);
            writer.WriteBoolean("think", false);
            writer.WriteStartArray("messages");
            WriteTextMessage(writer, "system", request.Context.SystemPolicy);
            if (request.Context.Constraints is { } constraints)
            {
                WriteTextMessage(writer, "system", JsonSerializer.Serialize(new
                {
                    HostTurnConstraints = constraints,
                    RemainingByteBudget = request.Budget,
                    request.Round,
                }));
            }
            if (request.Context.WebPages is { } pages)
            {
                WriteTextMessage(writer, "system", JsonSerializer.Serialize(new
                {
                    HostWebPageScope = pages,
                    pages.MaximumCalls,
                    pages.MaximumRounds,
                    RemainingByteBudget = request.Budget,
                    request.Round,
                }));
            }
            // Evidence stays serialized lower-trust context data; it is never promoted into system instructions.
            if (!request.Context.Evidence.IsEmpty)
            {
                WriteTextMessage(writer, "user", JsonSerializer.Serialize(new
                {
                    evidence = request.Context.Evidence.Select(item => new { content = item.Content }),
                }));
            }
            WriteTextMessage(writer, "user", request.Context.UserRequest);
            var index = 0;
            var exchangeIndex = 0;
            while (index < request.Messages.Length)
            {
                if (request.ClarificationAllowed && request.Messages[index] is ModelClarificationQuestion)
                {
                    WriteTextMessage(writer, "assistant", StrictUtf8.GetString(ModelToolProtocol.SerializeMessage(request.Messages[index++])));
                    WriteTextMessage(writer, "user", StrictUtf8.GetString(ModelToolProtocol.SerializeMessage(request.Messages[index++])));
                    continue;
                }
                if (exchangeIndex >= exchanges.Count || request.Messages[index++] is not ModelAssistantToolCalls calls)
                { throw new InvalidDataException("Native continuation contains an unobserved assistant exchange."); }
                var exchange = exchanges[exchangeIndex++];
                if (!ModelToolProtocol.SerializeMessage(calls).AsSpan()
                        .SequenceEqual(ModelToolProtocol.SerializeMessage(exchange.Calls)))
                { throw new InvalidDataException("Native continuation does not match the observed assistant exchange."); }
                writer.WriteRawValue(exchange.NativeMessage);
                using var native = ReadJson(exchange.NativeMessage);
                var nativeCalls = native.RootElement.GetProperty("tool_calls");
                for (var callIndex = 0; callIndex < calls.Calls.Length; callIndex++)
                {
                    if (index >= request.Messages.Length
                        || request.Messages[index++] is not ModelToolResultMessage result
                        || !string.Equals(result.CallId, calls.Calls[callIndex].CallId, StringComparison.Ordinal)
                        || !string.Equals(result.CapabilityId, calls.Calls[callIndex].CapabilityId, StringComparison.Ordinal))
                    { throw new InvalidDataException("Native tool result is missing or out of order."); }
                    var resultWire = WriteResult(result, nativeCalls[callIndex]);
                    writer.WriteRawValue(resultWire);
                }
            }
            if (exchangeIndex != exchanges.Count)
            { throw new InvalidDataException("The native conversation contains an unobserved or replayed exchange."); }
            writer.WriteEndArray();
            if (request.ClarificationAllowed) { WriteQuestionFormat(writer, request.Context.ToolMode); }
            if (request.Context.ToolMode != ModelToolMode.None)
            {
                writer.WriteStartArray("tools");
                foreach (var descriptor in request.Context.Tools) { WriteTool(writer, descriptor); }
                if (request.Context.McpTool is { } external)
                {
                    writer.WriteStartObject();
                    writer.WriteString("type", "function");
                    writer.WriteStartObject("function");
                    writer.WriteString("name", external.Id);
                    writer.WritePropertyName("parameters");
                    writer.WriteRawValue(external.InputSchema);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
        var bytes = stream.ToArray();
        if (bytes.Length > ModelToolProtocol.MaximumInputUtf8Bytes)
        { throw new InvalidDataException("The complete native request including framing exceeds its input limit."); }
        return bytes;
    }

    private static void WriteTextMessage(Utf8JsonWriter writer, string role, string content)
    {
        writer.WriteStartObject();
        writer.WriteString("role", role);
        writer.WriteString("content", content);
        writer.WriteEndObject();
    }

    private static byte[] WriteResult(ModelToolResultMessage result, JsonElement nativeCall)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("role", "tool");
            writer.WriteString("tool_name", result.CapabilityId);
            if (nativeCall.TryGetProperty("id", out var id)) { writer.WriteString("tool_call_id", id.GetString()); }
            writer.WriteString("content", StrictUtf8.GetString(result.Result.AsSpan()));
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }

    private static void WriteTool(Utf8JsonWriter writer, CapabilityDescriptor descriptor)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "function");
        writer.WriteStartObject("function");
        writer.WriteString("name", descriptor.Id);
        writer.WriteStartObject("parameters");
        writer.WriteString("type", "object");
        writer.WriteBoolean("additionalProperties", false);
        writer.WriteStartObject("properties");
        if (descriptor.Input == CapabilityInputShape.Page)
        {
            WriteInteger(writer, "offset", 0, null);
            WriteInteger(writer, "count", 1, descriptor.Limits.MaximumRecords);
        }
        else if (descriptor.Input == CapabilityInputShape.Id)
        {
            writer.WriteStartObject("id");
            writer.WriteString("type", "string");
            writer.WriteStartArray("enum");
            if (string.Equals(descriptor.Id, ReadOnlyCapabilityCatalog.Get, StringComparison.Ordinal))
            {
                foreach (var item in ReadOnlyCapabilityCatalog.Descriptors) { writer.WriteStringValue(item.Id); }
            }
            else { writer.WriteStringValue(ReadOnlyCapabilityCatalog.LocalRuntimeId); }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        else if (descriptor.Input == CapabilityInputShape.WebPageAddress)
        {
            writer.WriteStartObject("address");
            writer.WriteString("type", "string");
            writer.WriteEndObject();
        }
        else if (descriptor.Input == CapabilityInputShape.MetadataReference)
        {
            writer.WriteStartObject("ref");
            writer.WriteString("type", "string");
            writer.WriteNumber("minLength", 32);
            writer.WriteNumber("maxLength", 32);
            writer.WriteEndObject();
        }
        writer.WriteEndObject();
        if (descriptor.Input is CapabilityInputShape.Id or CapabilityInputShape.WebPageAddress or CapabilityInputShape.MetadataReference)
        {
            writer.WriteStartArray("required");
            writer.WriteStringValue(descriptor.Input == CapabilityInputShape.Id ? "id"
                : descriptor.Input == CapabilityInputShape.MetadataReference ? "ref" : "address");
            writer.WriteEndArray();
        }
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteInteger(Utf8JsonWriter writer, string name, int minimum, int? maximum)
    {
        writer.WriteStartObject(name);
        writer.WriteString("type", "integer");
        writer.WriteNumber("minimum", minimum);
        if (maximum is { } value) { writer.WriteNumber("maximum", value); }
        writer.WriteEndObject();
    }

    internal static Reply ReadReply(byte[] bytes, string model, int round, bool clarificationAllowed = false,
        Kora.Core.Mcp.McpModelTool? mcpTool = null, ModelToolMode toolMode = ModelToolMode.None)
    {
        using var document = ReadJson(bytes);
        var root = document.RootElement;
        ValidateReplyEnvelope(root, model, completed: true);
        var message = root.GetProperty("message");
        RequireFields(message, ["role", "content"], ["tool_calls", "thinking"]);
        if (!string.Equals(Text(message, "role"), "assistant", StringComparison.Ordinal)
            || message.TryGetProperty("thinking", out var thinking)
                && (thinking.ValueKind != JsonValueKind.String || thinking.GetString()!.Length != 0))
        { throw new InvalidDataException("Only a complete native assistant message without hidden reasoning is admitted."); }
        var content = Text(message, "content");
        if (clarificationAllowed && !message.TryGetProperty("tool_calls", out _))
        {
            return new(ModelToolProtocol.ParseAssistant(StrictUtf8.GetBytes(content), clarificationAllowed: true),
                JsonSerializer.SerializeToUtf8Bytes(message));
        }
        if (clarificationAllowed && toolMode != ModelToolMode.ReadOnly)
        { throw new InvalidDataException("Only an original read-only clarification turn may request tools."); }
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (message.TryGetProperty("tool_calls", out var calls))
            {
                if (calls.ValueKind != JsonValueKind.Array || calls.GetArrayLength() == 0 || content.Length != 0)
                { throw new InvalidDataException("A native tool-call message cannot contain an answer or an empty call list."); }
                writer.WriteString("kind", "toolCalls");
                writer.WriteStartArray("calls");
                var index = 0;
                foreach (var call in calls.EnumerateArray())
                {
                    RequireFields(call, ["function"], ["id", "type"]);
                    if (call.TryGetProperty("type", out var type)
                        && (type.ValueKind != JsonValueKind.String || !string.Equals(type.GetString(), "function", StringComparison.Ordinal)))
                    { throw new InvalidDataException("Unknown native tool-call type."); }
                    var function = call.GetProperty("function");
                    RequireFields(function, ["name", "arguments"], ["index"]);
                    if (function.TryGetProperty("index", out var position)
                        && (position.ValueKind != JsonValueKind.Number || !position.TryGetInt32(out var ordinal) || ordinal != index))
                    { throw new InvalidDataException("Native calls are out of order."); }
                    writer.WriteStartObject();
                    writer.WriteString("callId", call.TryGetProperty("id", out _)
                        ? Text(call, "id") : $"ollama_r{round.ToString(CultureInfo.InvariantCulture)}_c{index.ToString(CultureInfo.InvariantCulture)}");
                    writer.WriteString("capabilityId", Text(function, "name"));
                    writer.WritePropertyName("input");
                    function.GetProperty("arguments").WriteTo(writer);
                    writer.WriteEndObject();
                    index++;
                }
                writer.WriteEndArray();
            }
            else
            {
                writer.WriteString("kind", "answer");
                writer.WriteString("answer", content);
            }
            writer.WriteEndObject();
        }
        return new(ModelToolProtocol.ParseAssistant(stream.ToArray(), mcpTool: mcpTool), JsonSerializer.SerializeToUtf8Bytes(message));
    }

    private static void ValidateReplyEnvelope(JsonElement root, string model, bool completed)
    {
        RequireFields(root, ["model", "message", "done"],
            ["created_at", "done_reason", "total_duration", "load_duration", "prompt_eval_count",
                "prompt_eval_duration", "eval_count", "eval_duration"]);
        if (!string.Equals(Text(root, "model"), model, StringComparison.Ordinal)
            || root.GetProperty("done").ValueKind != (completed ? JsonValueKind.True : JsonValueKind.False))
        { throw new InvalidDataException("The native response is not a completed response from the host-selected model."); }
        foreach (var field in root.EnumerateObject())
        {
            if (field.Name.EndsWith("_duration", StringComparison.Ordinal) || field.Name.EndsWith("_count", StringComparison.Ordinal))
            {
                if (field.Value.ValueKind != JsonValueKind.Number || !field.Value.TryGetInt64(out var value) || value < 0)
                { throw new InvalidDataException("Invalid native response observations."); }
            }
        }
        if (root.TryGetProperty("created_at", out var created)
            && (created.ValueKind != JsonValueKind.String
                || !DateTimeOffset.TryParse(created.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out _)))
        { throw new InvalidDataException("Invalid native response timestamp."); }
        if (root.TryGetProperty("done_reason", out var reason)
            && (reason.ValueKind != JsonValueKind.String || !string.Equals(reason.GetString(), "stop", StringComparison.Ordinal)))
        { throw new InvalidDataException("The native response did not terminate normally."); }
    }

    private static string Text(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String) { throw new InvalidDataException("Native text has the wrong type."); }
        return value.GetString()!;
    }

    private static void RequireFields(JsonElement element, string[] required, string[]? optional = null)
    {
        if (element.ValueKind != JsonValueKind.Object
            || required.Any(name => !element.TryGetProperty(name, out _))
            || element.EnumerateObject().Any(field => !required.Contains(field.Name, StringComparer.Ordinal)
                && !(optional?.Contains(field.Name, StringComparer.Ordinal) ?? false)))
        { throw new InvalidDataException("The native message has missing or unknown fields."); }
    }
}
