using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Kora.Core.Dependencies;

/// <summary>Validates inert package transcripts against the authoritative context and canonical history.</summary>
public static class ModelToolChatMessages
{
    public static string Text(ModelToolMessage message) =>
        Encoding.UTF8.GetString(ModelToolProtocol.SerializeMessage(message));

    public static IDictionary<string, object?> Arguments(string input)
    {
        using var document = JsonDocument.Parse(input);
        return document.RootElement.EnumerateObject().ToDictionary(property => property.Name,
            property => (object?)property.Value.Clone(), StringComparer.Ordinal);
    }

    public static bool SameArguments(string input, IEnumerable<KeyValuePair<string, object?>>? arguments)
    {
        using var expected = JsonDocument.Parse(input);
        return JsonElement.DeepEquals(expected.RootElement,
            JsonSerializer.SerializeToElement(arguments?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)));
    }

    public static bool Matches(ModelToolCall call, FunctionCallContent function) =>
        string.Equals(function.CallId, call.CallId, StringComparison.Ordinal)
        && string.Equals(function.Name, call.CapabilityId, StringComparison.Ordinal)
        && SameArguments(call.InputJson, function.Arguments);

    public static bool MatchesQuestion(ModelClarificationQuestion question, FunctionCallContent function,
        string? questionCorrelation = null) =>
        string.Equals(function.CallId, question.CallId ?? questionCorrelation, StringComparison.Ordinal)
        && string.Equals(function.Name, ModelToolProtocol.AskUserToolName, StringComparison.Ordinal)
        && SameArguments(Encoding.UTF8.GetString(ModelToolProtocol.SerializeQuestionArguments(question.Question)), function.Arguments);

    public static void Validate(ModelToolContext context, IEnumerable<ModelToolMessage> history,
        IEnumerable<ChatMessage> messages, string? questionCorrelation = null)
    {
        var contents = messages.SelectMany(message => message.Contents.Select(content => (message.Role, Content: content))).ToArray();
        if (contents.Length == 0 || contents[0].Role != ChatRole.User || contents[0].Content is not TextContent original
            || !string.Equals(original.Text, context.UserRequest, StringComparison.Ordinal))
        { throw new InvalidDataException("The package transcript changed the original USER input."); }
        var index = 1;
        foreach (var item in history)
        {
            if (item is ModelAssistantToolCalls batch)
            {
                foreach (var call in batch.Calls)
                {
                    if (index >= contents.Length || contents[index].Role != ChatRole.Assistant
                        || contents[index++].Content is not FunctionCallContent function || !Matches(call, function))
                    { throw new InvalidDataException("The package transcript changed a canonical tool call."); }
                }
            }
            else if (item is ModelClarificationQuestion question)
            {
                if (question.CallId is null && (index >= contents.Length || contents[index].Role != ChatRole.Assistant
                    || contents[index++].Content is not TextContent text
                    || !string.Equals(text.Text, Text(question), StringComparison.Ordinal)))
                { throw new InvalidDataException("The package transcript changed the canonical question text."); }
                if (index >= contents.Length || contents[index].Role != ChatRole.Assistant
                    || contents[index++].Content is not FunctionCallContent function
                    || !MatchesQuestion(question, function, questionCorrelation))
                { throw new InvalidDataException("The package transcript changed the canonical question call."); }
            }
            else if (item is ModelQuestionAnswerMessage answer)
            {
                if (answer.Question.CallId is null)
                {
                    if (index >= contents.Length || contents[index].Role != ChatRole.User
                        || contents[index++].Content is not TextContent text
                        || !string.Equals(text.Text, Text(answer), StringComparison.Ordinal))
                    { throw new InvalidDataException("The package transcript changed the canonical USER answer."); }
                }
                else if (index >= contents.Length || contents[index].Role != ChatRole.Tool
                    || contents[index++].Content is not FunctionResultContent function
                    || !string.Equals(function.CallId, answer.Question.CallId, StringComparison.Ordinal)
                    || !ReferenceEquals(function.Result, answer) || function.Exception is not null)
                { throw new InvalidDataException("The package transcript replaced the host question answer."); }
            }
            else
            {
                var result = (ModelToolResultMessage)item;
                if (index >= contents.Length || contents[index].Role != ChatRole.Tool
                    || contents[index++].Content is not FunctionResultContent function
                    || !string.Equals(function.CallId, result.CallId, StringComparison.Ordinal)
                    || !ReferenceEquals(function.Result, result) || function.Exception is not null)
                { throw new InvalidDataException("The package transcript replaced a host tool result."); }
            }
        }
        if (index != contents.Length)
        { throw new InvalidDataException("The package transcript contains input outside canonical history."); }
    }
}
