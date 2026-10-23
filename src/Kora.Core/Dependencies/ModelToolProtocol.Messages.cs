using System.Collections.Immutable;
using System.Text.Json;
using Kora.Core.Tools;

namespace Kora.Core.Dependencies;

public static partial class ModelToolProtocol
{
    public static ModelAssistantMessage ParseAssistant(ReadOnlySpan<byte> wire, bool clarificationAllowed = false)
    {
        using var document = ReadJson(wire, MaximumOutputUtf8Bytes);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("kind", out var kind)
            || kind.ValueKind != JsonValueKind.String)
        { throw new InvalidDataException("An assistant message requires an exact kind."); }
        if (string.Equals(kind.GetString(), "answer", StringComparison.Ordinal))
        {
            RequireFields(root, "kind", "answer");
            var answer = ReadString(root, "answer");
            if (string.IsNullOrWhiteSpace(answer)
                || JsonSerializer.SerializeToUtf8Bytes(new LocalModelResponse(answer, null)).Length > MaximumOutputUtf8Bytes)
            { throw new InvalidDataException("The complete final answer is empty or exceeds the host response limit."); }
            return new ModelFinalResponse(answer);
        }
        if (clarificationAllowed && string.Equals(kind.GetString(), "question", StringComparison.Ordinal))
        { return ReadQuestion(root); }
        if (!string.Equals(kind.GetString(), "toolCalls", StringComparison.Ordinal))
        { throw new InvalidDataException("Unsupported assistant message kind."); }
        RequireFields(root, "kind", "calls");
        var calls = root.GetProperty("calls");
        if (calls.ValueKind != JsonValueKind.Array || calls.GetArrayLength() is < 1
            || calls.GetArrayLength() > MaximumCalls)
        { throw new InvalidDataException("The assistant tool-call count exceeds the finite limit."); }
        var result = ImmutableArray.CreateBuilder<ModelToolCall>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var call in calls.EnumerateArray())
        {
            RequireFields(call, "callId", "capabilityId", "input");
            var callId = ReadString(call, "callId");
            var capabilityId = ReadString(call, "capabilityId");
            if (!ValidCallId(callId) || !ids.Add(callId))
            { throw new InvalidDataException("Tool-call correlation must be unique bounded ASCII, not host identity."); }
            var descriptor = ReadOnlyCapabilityCatalog.FindExact(capabilityId);
            if (descriptor is null || descriptor.Availability != CapabilityAvailability.Available
                || !descriptor.CallerLanes.Contains(CapabilityLane.Execution))
            { throw new InvalidDataException("The requested capability is not in the available execution catalogue."); }
            var input = call.GetProperty("input").GetRawText();
            if (CapabilityInputPolicy.Read(descriptor, input).DenialReason is not null)
            { throw new InvalidDataException("The complete capability input is not admitted."); }
            result.Add(new(callId, capabilityId, input));
        }
        return new ModelAssistantToolCalls(result.ToImmutable());
    }

    public static ModelToolResultMessage CreateToolResult(ModelToolCall call, CapabilityReply reply)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(reply);
        if (reply.Outcome != CapabilityOutcome.Succeeded || string.IsNullOrWhiteSpace(reply.Reason)
            || !CapabilityReplyWire.IsWithinLimit(reply))
        { throw new InvalidDataException("The host tool result is invalid or exceeds its complete bound."); }
        ValidateResult(call, reply);
        var wire = CapabilityReplyWire.Serialize(reply);
        using var document = ReadJson(wire, MaximumOutputUtf8Bytes);
        return new(call.CallId, call.CapabilityId, ImmutableArray.CreateRange(wire));
    }

    public static byte[] SerializeMessage(ModelToolMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) { WriteMessage(writer, message); }
        return stream.ToArray();
    }

    private static bool ValidCallId(string id) =>
        id.Length is >= 1 and <= 64 && id.Any(character => char.IsAsciiLetterOrDigit(character) && character != '0')
        && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private static void WriteMessage(Utf8JsonWriter writer, ModelToolMessage message)
    {
        writer.WriteStartObject();
        if (message is ModelFinalResponse final)
        {
            writer.WriteString("kind", "answer");
            writer.WriteString("answer", final.Answer);
        }
        else if (message is ModelAssistantToolCalls assistant)
        {
            writer.WriteString("kind", "toolCalls");
            writer.WriteStartArray("calls");
            foreach (var call in assistant.Calls)
            {
                writer.WriteStartObject();
                writer.WriteString("callId", call.CallId);
                writer.WriteString("capabilityId", call.CapabilityId);
                writer.WritePropertyName("input");
                writer.WriteRawValue(call.InputJson);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
        }
        else if (message is ModelClarificationQuestion question)
        {
            writer.WriteString("kind", "question");
            writer.WriteString("prompt", question.Question.Prompt);
            writer.WriteStartArray("options");
            foreach (var label in question.Question.Options) { writer.WriteStringValue(label); }
            writer.WriteEndArray();
        }
        else if (message is ModelQuestionAnswerMessage answer)
        {
            writer.WriteString("kind", "clarificationAnswer");
            writer.WriteStartArray("choices");
            foreach (var choice in answer.Answer.Choices) { writer.WriteStringValue(choice); }
            writer.WriteEndArray();
            writer.WriteString("text", answer.Answer.Text);
            writer.WriteString("channel", answer.Channel.ToString());
        }
        else
        {
            // The closed hierarchy's remaining concrete message is the host result.
            var result = (ModelToolResultMessage)message;
            writer.WriteString("kind", "toolResult");
            writer.WriteString("callId", result.CallId);
            writer.WriteString("capabilityId", result.CapabilityId);
            writer.WritePropertyName("result");
            writer.WriteRawValue(result.Result.AsSpan());
        }
        writer.WriteEndObject();
    }

    private static void ValidateHistory(ImmutableArray<ModelToolMessage> messages)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;
        while (index < messages.Length)
        {
            if (messages[index++] is not ModelAssistantToolCalls assistant
                || assistant.Calls.Length > MaximumCalls - ids.Count)
            { throw new InvalidDataException("History must contain only complete serial tool exchanges."); }
            foreach (var call in assistant.Calls)
            {
                if (!ids.Add(call.CallId) || index >= messages.Length
                    || messages[index++] is not ModelToolResultMessage result
                    || !string.Equals(result.CallId, call.CallId, StringComparison.Ordinal)
                    || !string.Equals(result.CapabilityId, call.CapabilityId, StringComparison.Ordinal))
                { throw new InvalidDataException("Tool results must correlate exactly once to the complete assistant message."); }
            }
        }
    }
}
