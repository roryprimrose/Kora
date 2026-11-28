using System.Globalization;
using System.Text;
using System.Text.Json;
using Kora.Core.Dependencies;

namespace Kora.Windows.Dependencies;

/// <summary>Strict raw provider admission before SDK decoding; canonical policy remains Core-owned.</summary>
internal static partial class OllamaToolWire
{
    internal sealed record Exchange(ModelAssistantToolCalls Calls, byte[] NativeMessage);
    internal sealed record Reply(ModelAssistantMessage Message, byte[] NativeMessage);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static ModelTokenUsage? ReadTokenUsage(byte[] bytes, string model)
    {
        using var document = ReadJson(bytes);
        ValidateReplyEnvelope(document.RootElement, model, completed: true);
        return ReadTokenUsage(document.RootElement);
    }

    private static ModelTokenUsage? ReadTokenUsage(JsonElement root)
    {
        long? input = root.TryGetProperty("prompt_eval_count", out var prompt) ? prompt.GetInt64() : null;
        long? output = root.TryGetProperty("eval_count", out var generated) ? generated.GetInt64() : null;
        return input is null && output is null ? null : new(input, output, input);
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
            var structured = ModelToolProtocol.ParseAssistant(StrictUtf8.GetBytes(content), clarificationAllowed: true,
                formsAllowed: false);
            if (structured is ModelClarificationQuestion { CallId: not null })
            { throw new InvalidDataException("Only a native ask_user call can establish tool correlation."); }
            return new(structured, JsonSerializer.SerializeToUtf8Bytes(message));
        }
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
                    if (clarificationAllowed && string.Equals(Text(function, "name"), ModelToolProtocol.AskUserToolName, StringComparison.Ordinal))
                    {
                        if (calls.GetArrayLength() != 1)
                        { throw new InvalidDataException("An ask_user wait cannot share a batch with another tool call."); }
                        var questionId = call.TryGetProperty("id", out _)
                            ? Text(call, "id") : $"ollama_r{round.ToString(CultureInfo.InvariantCulture)}_c0";
                        return new(ModelToolProtocol.ParseAskUser(StrictUtf8.GetBytes(function.GetProperty("arguments").GetRawText()), questionId,
                                formsAllowed: toolMode == ModelToolMode.None),
                            JsonSerializer.SerializeToUtf8Bytes(message));
                    }
                    if (clarificationAllowed && toolMode is not (ModelToolMode.ReadOnly or ModelToolMode.InteractiveConversation))
                    { throw new InvalidDataException("Only an admitted observation-capable clarification turn may request tools."); }
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
            ["created_at", "done_reason", "total_duration", "load_duration", "prompt_eval_count", "prompt_eval_cached_count",
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
