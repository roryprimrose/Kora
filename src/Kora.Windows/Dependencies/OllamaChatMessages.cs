using System.Globalization;
using System.Text;
using System.Text.Json;
using Kora.Core.Dependencies;
using Kora.Core.Tools;
using Microsoft.Extensions.AI;
using OllamaSharp.Models.Chat;
using NativeMessage = OllamaSharp.Models.Chat.Message;
using ChatRole = Microsoft.Extensions.AI.ChatRole;

namespace Kora.Windows.Dependencies;

internal static class OllamaChatMessages
{
    internal sealed record NativeRequest(ChatRequest Request, IReadOnlyDictionary<int, string> ResultCallIds);

    internal static List<ChatMessage> CreateContext(ModelToolRequest request)
    {
        List<ChatMessage> messages = [new(ChatRole.System, request.Context.SystemPolicy)];
        if (request.Context.Constraints is { } constraints)
        {
            messages.Add(new(ChatRole.System, JsonSerializer.Serialize(new
            {
                HostTurnConstraints = constraints,
                RemainingByteBudget = request.Budget,
                request.Round,
            })));
        }
        if (request.Context.WebPages is { } pages)
        {
            messages.Add(new(ChatRole.System, JsonSerializer.Serialize(new
            {
                HostWebPageScope = pages,
                pages.MaximumCalls,
                pages.MaximumRounds,
                RemainingByteBudget = request.Budget,
                request.Round,
            })));
        }
        if (!request.Context.Evidence.IsEmpty)
        {
            messages.Add(new(ChatRole.User, JsonSerializer.Serialize(new
            {
                evidence = request.Context.Evidence.Select(item => new { content = item.Content }),
            })));
        }
        messages.Add(new(ChatRole.User, request.Context.UserRequest));
        return messages;
    }

    internal static NativeRequest CreateRequest(ModelToolRequest request, string model,
        IReadOnlyList<OllamaToolWire.Exchange> exchanges)
    {
        List<NativeMessage> messages = CreateContext(request).Select(message => new NativeMessage
        {
            Role = new OllamaSharp.Models.Chat.ChatRole(message.Role.Value),
            Content = message.Text,
        }).ToList();
        Dictionary<int, string> resultIds = [];
        var index = 0;
        var exchangeIndex = 0;
        while (index < request.Messages.Length)
        {
            if (request.ClarificationAllowed && request.Messages[index] is ModelClarificationQuestion question)
            {
                index++;
                var answer = (ModelQuestionAnswerMessage)request.Messages[index++];
                if (question.CallId is { } callId)
                {
                    messages.Add(new()
                    {
                        Role = OllamaSharp.Models.Chat.ChatRole.Assistant,
                        Content = string.Empty,
                        ToolCalls = [new NativeMessage.ToolCall
                        {
                            Id = callId,
                            Function = new() { Name = ModelToolProtocol.AskUserToolName,
                                Arguments = Arguments(ModelToolProtocol.SerializeQuestionArguments(question.Question)) },
                        }],
                    });
                    AddResult(ModelToolProtocol.AskUserToolName, callId, Encoding.UTF8.GetString(ModelToolProtocol.SerializeMessage(answer)));
                }
                else
                {
                    messages.Add(new() { Role = OllamaSharp.Models.Chat.ChatRole.Assistant,
                        Content = Encoding.UTF8.GetString(ModelToolProtocol.SerializeMessage(question)) });
                    messages.Add(new() { Role = OllamaSharp.Models.Chat.ChatRole.User,
                        Content = Encoding.UTF8.GetString(ModelToolProtocol.SerializeMessage(answer)) });
                }
                continue;
            }
            if (exchangeIndex >= exchanges.Count || request.Messages[index++] is not ModelAssistantToolCalls calls)
            { throw new InvalidDataException("Native continuation contains an unobserved assistant exchange."); }
            var exchange = exchanges[exchangeIndex++];
            if (!ModelToolProtocol.SerializeMessage(calls).AsSpan().SequenceEqual(ModelToolProtocol.SerializeMessage(exchange.Calls)))
            { throw new InvalidDataException("Native continuation differs from the observed assistant exchange."); }
            var native = JsonSerializer.Deserialize<NativeMessage>(exchange.NativeMessage)
                ?? throw new InvalidDataException("The observed native message is missing.");
            var nativeCalls = native.ToolCalls?.ToArray()
                ?? throw new InvalidDataException("The observed native calls are missing.");
            messages.Add(native);
            for (var callIndex = 0; callIndex < calls.Calls.Length; callIndex++)
            {
                if (index >= request.Messages.Length || request.Messages[index++] is not ModelToolResultMessage result
                    || !string.Equals(result.CallId, calls.Calls[callIndex].CallId, StringComparison.Ordinal)
                    || !string.Equals(result.CapabilityId, calls.Calls[callIndex].CapabilityId, StringComparison.Ordinal))
                { throw new InvalidDataException("Native tool result is missing or out of order."); }
                AddResult(result.CapabilityId, nativeCalls[callIndex].Id, Encoding.UTF8.GetString(result.Result.AsSpan()));
            }
        }
        if (exchangeIndex != exchanges.Count)
        { throw new InvalidDataException("The native history contains an unobserved exchange."); }

        List<object> tools = [];
        if (request.ClarificationAllowed && !request.Messages.OfType<ModelClarificationQuestion>().Any())
        {
            using var schema = JsonDocument.Parse(request.FormsAllowed
                ? ModelToolProtocol.FormAskUserInputSchema : ModelToolProtocol.AskUserInputSchema);
            tools.Add(new { type = "function", function = new
            {
                name = ModelToolProtocol.AskUserToolName,
                description = ModelToolProtocol.AskUserDescription + (request.FormsAllowed
                    ? " Alternatively supply prompt and one to four required fields, each bounded text or singleChoice. "
                        + "Do not combine fields with top-level options or selection bounds. No defaults or credentials." : string.Empty),
                parameters = schema.RootElement.Clone(),
            } });
        }
        foreach (var descriptor in request.Context.Tools)
        {
            using var memory = new MemoryStream();
            using (var writer = new Utf8JsonWriter(memory)) { CapabilityInputSchema.Write(writer, descriptor); }
            using var schema = JsonDocument.Parse(memory.ToArray());
            tools.Add(new { type = "function", function = new { name = descriptor.Id, parameters = schema.RootElement.Clone() } });
        }
        if (request.Context.McpTool is { } external)
        {
            using var schema = JsonDocument.Parse(external.InputSchema);
            tools.Add(new { type = "function", function = new { name = external.Id, parameters = schema.RootElement.Clone() } });
        }
        return new(new()
        {
            Model = model, Stream = false, Think = false, Messages = messages, Tools = tools,
            Format = request.ClarificationAllowed ? OllamaToolWire.QuestionFormat(request.Context.ToolMode,
                includeQuestion: request.Context.ToolMode != ModelToolMode.InteractiveConversation
                    || !request.Messages.OfType<ModelClarificationQuestion>().Any()) : null,
        }, resultIds);

        void AddResult(string name, string? callId, string content)
        {
            if (callId is not null) { resultIds.Add(messages.Count, callId); }
            messages.Add(new() { Role = OllamaSharp.Models.Chat.ChatRole.Tool, ToolName = name, Content = content });
        }
    }

    internal static ModelAssistantMessage Project(ChatDoneResponseStream response, ModelToolRequest request)
    {
        var message = response.Message;
        var calls = message.ToolCalls?.ToArray();
        if (calls is { Length: > 0 })
        {
            string Id(NativeMessage.ToolCall call, int index) => call.Id
                ?? $"ollama_r{request.Round.ToString(CultureInfo.InvariantCulture)}_c{index.ToString(CultureInfo.InvariantCulture)}";
            if (request.ClarificationAllowed && calls.Length == 1
                && calls[0].Function?.Name is ModelToolProtocol.AskUserToolName)
            {
                return ModelToolProtocol.ParseAskUser(JsonSerializer.SerializeToUtf8Bytes(calls[0].Function!.Arguments),
                    Id(calls[0], 0), formsAllowed: request.Context.ToolMode == ModelToolMode.None);
            }
            return ModelToolProtocol.ParseAssistant(JsonSerializer.SerializeToUtf8Bytes(new
            {
                kind = "toolCalls",
                calls = calls.Select((call, index) => new
                {
                    callId = Id(call, index),
                    capabilityId = call.Function?.Name,
                    input = call.Function?.Arguments,
                }),
            }), mcpTool: request.Context.McpTool);
        }
        return request.ClarificationAllowed
            ? ModelToolProtocol.ParseAssistant(Encoding.UTF8.GetBytes(message.Content!), clarificationAllowed: true, formsAllowed: false)
            : ModelToolProtocol.ParseAssistant(JsonSerializer.SerializeToUtf8Bytes(new { kind = "answer", answer = message.Content }));
    }

    private static Dictionary<string, object?> Arguments(byte[] bytes) =>
        JsonSerializer.Deserialize<Dictionary<string, object?>>(bytes)
            ?? throw new InvalidDataException("Native tool input is missing.");
}
