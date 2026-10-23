using System.Text.Json;
using Kora.Core.Dependencies;

namespace Kora.Windows.Dependencies;

internal static partial class OllamaToolWire
{
    private static void WriteQuestionHistory(Utf8JsonWriter writer, ModelToolRequest request)
    {
        foreach (var message in request.Messages)
        {
            WriteTextMessage(writer, message is ModelClarificationQuestion ? "assistant" : "user",
                StrictUtf8.GetString(ModelToolProtocol.SerializeMessage(message)));
        }
    }

    private static void WriteQuestionFormat(Utf8JsonWriter writer)
    {
        // This exact negotiated structured response is not the ordinary assistant prose channel.
        writer.WritePropertyName("format");
        writer.WriteRawValue("""
            {"oneOf":[
              {"type":"object","properties":{"kind":{"const":"answer"},"answer":{"type":"string"}},"required":["kind","answer"],"additionalProperties":false},
              {"type":"object","properties":{"kind":{"const":"question"},"prompt":{"type":"string","maxLength":500},"options":{"type":"array","maxItems":4,"items":{"type":"string","maxLength":80}}},"required":["kind","prompt","options"],"additionalProperties":false}
            ]}
            """);
    }
}
