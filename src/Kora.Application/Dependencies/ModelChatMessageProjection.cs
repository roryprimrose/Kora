using System.Text;
using System.Text.Json;
using Kora.Core.Dependencies;
using Microsoft.Extensions.AI;

namespace Kora.Application.Dependencies;

internal static class ModelChatMessageProjection
{
    internal static string Text(ModelToolMessage message) =>
        Encoding.UTF8.GetString(ModelToolProtocol.SerializeMessage(message));

    internal static Dictionary<string, object?> Arguments(string input)
    {
        using var document = JsonDocument.Parse(input);
        return document.RootElement.EnumerateObject().ToDictionary(property => property.Name,
            property => (object?)property.Value.Clone(), StringComparer.Ordinal);
    }

    internal static bool SameArguments(string input, IEnumerable<KeyValuePair<string, object?>>? arguments)
    {
        using var expected = JsonDocument.Parse(input);
        return JsonElement.DeepEquals(expected.RootElement,
            JsonSerializer.SerializeToElement(arguments?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)));
    }

    internal static bool Matches(ModelToolCall call, FunctionCallContent function) =>
        string.Equals(function.CallId, call.CallId, StringComparison.Ordinal)
        && string.Equals(function.Name, call.CapabilityId, StringComparison.Ordinal)
        && SameArguments(call.InputJson, function.Arguments);

    // This changes only the package transcript: non-tool question answers remain USER data.
    internal sealed class QuestionContinuationClient(IChatClient inner) : FunctionInvokingChatClient(inner)
    {
        protected override IList<ChatMessage> CreateResponseMessages(ReadOnlySpan<FunctionInvocationResult> results)
        {
            if (results.Length == 1 && results[0].Exception is null
                && results[0].Result is ModelQuestionAnswerMessage { Question.CallId: null } answer)
            { return [new(ChatRole.User, Text(answer))]; }
            return base.CreateResponseMessages(results);
        }
    }
}
