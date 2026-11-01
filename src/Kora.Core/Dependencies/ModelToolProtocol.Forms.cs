using System.Globalization;
using System.Text.Json;

using Kora.Core.Interaction;

namespace Kora.Core.Dependencies;

public static partial class ModelToolProtocol
{
    public const int FormClarificationSchemaVersion = 4;
    public const string FormAskUserInputSchema = """
        {"oneOf":[
        """ + AskUserInputSchema + """
        ,{"type":"object","properties":{
          "prompt":{"type":"string","minLength":1,"maxLength":500},
          "fields":{"type":"array","minItems":1,"maxItems":4,"items":{"oneOf":[
            {"type":"object","properties":{
              "label":{"type":"string","minLength":1,"maxLength":80},"type":{"const":"text"},
              "maximumLength":{"type":"integer","minimum":1,"maximum":500},
              "maximumUtf8Bytes":{"type":"integer","minimum":1,"maximum":2000}
            },"required":["label","type","maximumLength","maximumUtf8Bytes"],"additionalProperties":false},
            {"type":"object","properties":{
              "label":{"type":"string","minLength":1,"maxLength":80},"type":{"const":"singleChoice"},
              "options":{"type":"array","minItems":2,"maxItems":4,"items":{"type":"string","minLength":1,"maxLength":80}}
            },"required":["label","type","options"],"additionalProperties":false}
          ]}}
        },"required":["prompt","fields"],"additionalProperties":false}]}
        """;

    public static bool AllowsForms(ModelToolMode mode, ModelProviderIdentity provider) =>
        mode == ModelToolMode.None && provider == ModelProviderIdentity.Ollama;

    private static ModelClarificationQuestion ReadForm(JsonElement root, string callId)
    {
        var fields = root.GetProperty("fields");
        if (fields.ValueKind != JsonValueKind.Array || fields.GetArrayLength() is < 1 or > QuestionField.MaximumFields)
        { throw new InvalidDataException("A clarification form requires one to four fields."); }
        var result = new List<QuestionField>();
        try
        {
            foreach (var field in fields.EnumerateArray())
            {
                var type = ReadString(field, "type");
                var id = "field-" + result.Count.ToString(CultureInfo.InvariantCulture);
                if (string.Equals(type, "text", StringComparison.Ordinal))
                {
                    RequireFields(field, "label", "type", "maximumLength", "maximumUtf8Bytes");
                    if (!field.GetProperty("maximumLength").TryGetInt32(out var length)
                        || !field.GetProperty("maximumUtf8Bytes").TryGetInt32(out var bytes))
                    { throw new InvalidDataException("Text field bounds must be integers."); }
                    result.Add(new(id, ReadString(field, "label"), QuestionKind.Text, [], length, bytes));
                }
                else if (string.Equals(type, "singleChoice", StringComparison.Ordinal))
                {
                    RequireFields(field, "label", "type", "options");
                    var options = field.GetProperty("options");
                    if (options.ValueKind != JsonValueKind.Array || options.GetArrayLength() is < 2 or > 4)
                    { throw new InvalidDataException("A field requires two to four choices."); }
                    result.Add(new(id, ReadString(field, "label"), QuestionKind.SingleChoice,
                        options.EnumerateArray().Select((option, index) => option.ValueKind == JsonValueKind.String
                            ? new QuestionOption("option-" + index.ToString(CultureInfo.InvariantCulture), option.GetString()!)
                            : throw new InvalidDataException("Field choices must be strings."))));
                }
                else { throw new InvalidDataException("Unknown clarification field type."); }
            }
            return new(new(ReadString(root, "prompt"), [], Fields: result), callId);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException
            or Kora.Core.Conversation.ConversationCredentialException)
        { throw new InvalidDataException("The complete clarification form is malformed or contains refused content.", exception); }
    }

    private static void WriteFormFields(Utf8JsonWriter writer, IReadOnlyList<QuestionField> fields)
    {
        writer.WriteStartArray("fields");
        foreach (var field in fields)
        {
            writer.WriteStartObject();
            writer.WriteString("label", field.Label);
            writer.WriteString("type", field.Kind == QuestionKind.Text ? "text" : "singleChoice");
            if (field.Kind == QuestionKind.Text)
            {
                writer.WriteNumber("maximumLength", field.MaximumLength);
                writer.WriteNumber("maximumUtf8Bytes", field.MaximumUtf8Bytes);
            }
            else
            {
                writer.WriteStartArray("options");
                foreach (var option in field.Options) { writer.WriteStringValue(option.Label); }
                writer.WriteEndArray();
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    public static byte[] SerializeQuestionArguments(LocalModelQuestion question)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("prompt", question.Prompt);
            if (question.Fields is { } fields) { WriteFormFields(writer, fields); }
            else
            {
                writer.WriteStartArray("options");
                foreach (var option in question.Options) { writer.WriteStringValue(option); }
                writer.WriteEndArray();
                writer.WriteNumber("minimum", question.Minimum);
                writer.WriteNumber("maximum", question.Maximum);
            }
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }
}
