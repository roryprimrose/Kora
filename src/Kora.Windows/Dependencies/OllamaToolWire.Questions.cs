using System.Text.Json;
using Kora.Core.Dependencies;

namespace Kora.Windows.Dependencies;

internal static partial class OllamaToolWire
{
    private static void WriteAskUserTool(Utf8JsonWriter writer, bool formsAllowed)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "function");
        writer.WriteStartObject("function");
        writer.WriteString("name", ModelToolProtocol.AskUserToolName);
        writer.WriteString("description", ModelToolProtocol.AskUserDescription
            + (formsAllowed ? " Alternatively supply prompt and one to four required fields, each bounded text or singleChoice. "
                + "Do not combine fields with top-level options or selection bounds. No defaults or credentials." : string.Empty));
        writer.WritePropertyName("parameters");
        writer.WriteRawValue(formsAllowed ? ModelToolProtocol.FormAskUserInputSchema : ModelToolProtocol.AskUserInputSchema);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteQuestionExchange(Utf8JsonWriter writer, ModelClarificationQuestion question,
        ModelQuestionAnswerMessage answer)
    {
        writer.WriteStartObject();
        writer.WriteString("role", "assistant");
        writer.WriteString("content", string.Empty);
        writer.WriteStartArray("tool_calls");
        writer.WriteStartObject();
        writer.WriteString("id", question.CallId);
        writer.WriteString("type", "function");
        writer.WriteStartObject("function");
        writer.WriteString("name", ModelToolProtocol.AskUserToolName);
        writer.WritePropertyName("arguments");
        writer.WriteRawValue(ModelToolProtocol.SerializeQuestionArguments(question.Question));
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
        writer.WriteStartObject();
        writer.WriteString("role", "tool");
        writer.WriteString("tool_name", ModelToolProtocol.AskUserToolName);
        writer.WriteString("tool_call_id", question.CallId);
        writer.WriteString("content", StrictUtf8.GetString(ModelToolProtocol.SerializeMessage(answer)));
        writer.WriteEndObject();
    }

    private static void WriteQuestionFormat(Utf8JsonWriter writer, ModelToolMode mode)
    {
        // This exact negotiated structured response is not the ordinary assistant prose channel.
        writer.WritePropertyName("format");
        writer.WriteStartObject();
        writer.WriteStartArray("oneOf");
        writer.WriteRawValue("""
            {"type":"object","properties":{"kind":{"const":"answer"},"answer":{"type":"string"}},"required":["kind","answer"],"additionalProperties":false}
            """);
        writer.WriteStartObject();
        writer.WriteString("type", "object");
        writer.WriteStartObject("properties");
        writer.WriteStartObject("kind");
        writer.WriteString("const", "question");
        writer.WriteEndObject();
        writer.WritePropertyName("prompt");
        writer.WriteRawValue("""{"type":"string","maxLength":500}""");
        writer.WriteStartObject("options");
        writer.WriteString("type", "array");
        if (mode == ModelToolMode.ReadOnly) { writer.WriteNumber("minItems", 2); }
        writer.WriteNumber("maxItems", 4);
        writer.WritePropertyName("items");
        writer.WriteRawValue("""{"type":"string","maxLength":80}""");
        writer.WriteEndObject();
        writer.WriteEndObject();
        writer.WriteStartArray("required");
        writer.WriteStringValue("kind");
        writer.WriteStringValue("prompt");
        writer.WriteStringValue("options");
        writer.WriteEndArray();
        writer.WriteBoolean("additionalProperties", false);
        writer.WriteEndObject();
        writer.WriteEndArray();
        writer.WriteEndObject();
    }
}
