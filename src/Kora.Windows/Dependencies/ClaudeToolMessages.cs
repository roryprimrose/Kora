using System.Text;
using System.Text.Json;
using Anthropic.Models.Messages;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Microsoft.Extensions.AI;

namespace Kora.Windows.Dependencies;

/// <summary>Maps the admitted host protocol to inert MEAI messages and declarations, never tool execution.</summary>
internal static class ClaudeToolMessages
{
    internal static string Name(string id) => "kora_" + id.Replace('.', '_');

    internal static List<ChatMessage> CreateMessages(ModelToolRequest request)
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.System, request.Context.SystemPolicy),
            new(ChatRole.User, request.Context.UserRequest),
        ];
        for (var index = 0; index < request.Messages.Length; index++)
        {
            switch (request.Messages[index])
            {
                case ModelAssistantToolCalls assistant:
                    messages.Add(new(ChatRole.Assistant, assistant.Calls.Select(call =>
                        (AIContent)new FunctionCallContent(call.CallId, Name(call.CapabilityId), Arguments(call.InputJson))).ToList()));
                    break;
                case ModelToolResultMessage result:
                    List<AIContent> results = [new FunctionResultContent(result.CallId, Encoding.UTF8.GetString(result.Result.AsSpan()))];
                    while (index + 1 < request.Messages.Length && request.Messages[index + 1] is ModelToolResultMessage next)
                    {
                        results.Add(new FunctionResultContent(next.CallId, Encoding.UTF8.GetString(next.Result.AsSpan())));
                        index++;
                    }
                    messages.Add(new(ChatRole.Tool, results));
                    break;
                case ModelClarificationQuestion question when request.ClarificationAllowed:
                    messages.Add(new(ChatRole.Assistant,
                    [
                        new FunctionCallContent(question.CallId!, ModelToolProtocol.AskUserToolName,
                            new Dictionary<string, object?>(StringComparer.Ordinal)
                            {
                                ["prompt"] = question.Question.Prompt, ["options"] = question.Question.Options,
                                ["minimum"] = question.Question.Minimum, ["maximum"] = question.Question.Maximum,
                            }),
                    ]));
                    break;
                case ModelQuestionAnswerMessage answer when request.ClarificationAllowed:
                    messages.Add(new(ChatRole.Tool,
                    [
                        new FunctionResultContent(answer.Question.CallId!,
                            Encoding.UTF8.GetString(ModelToolProtocol.SerializeMessage(answer))),
                    ]));
                    break;
                default:
                    throw new InvalidDataException("Only canonical admitted history can enter Claude.");
            }
        }
        return messages;
    }

    internal static List<AITool> CreateTools(ModelToolRequest request)
    {
        List<AITool> tools = [];
        if (request.ClarificationAllowed)
        {
            using var question = JsonDocument.Parse(ModelToolProtocol.AskUserInputSchema);
            tools.Add(Declaration(ModelToolProtocol.AskUserToolName, ModelToolProtocol.AskUserDescription, question.RootElement));
        }
        foreach (var descriptor in request.Context.Tools)
        {
            using var memory = new MemoryStream();
            using (var writer = new Utf8JsonWriter(memory))
            { CapabilityInputSchema.Write(writer, descriptor, includeEmptyRequired: true, boundedOffset: true); }
            using var schema = JsonDocument.Parse(memory.ToArray());
            tools.Add(Declaration(Name(descriptor.Id), descriptor.Effect == CapabilityEffect.NativePresentation
                ? SettingsNavigationTool.ModelPolicy : "Host-owned read-only observation: " + descriptor.Id, schema.RootElement));
        }
        return tools;
    }

    private static AITool Declaration(string name, string description, JsonElement schema) =>
        new ToolUnion(new Tool
        {
            Name = name, Description = description,
            InputSchema = new InputSchema(schema.EnumerateObject().ToDictionary(property => property.Name,
                property => property.Value.Clone(), StringComparer.Ordinal)),
        }).AsAITool();

    private static Dictionary<string, object?> Arguments(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.EnumerateObject().ToDictionary(property => property.Name,
            property => (object?)property.Value.Clone(), StringComparer.Ordinal);
    }

    internal static void Validate(byte[] bytes, ProviderProfileTuple profile, int outputLimit, int inputLimit,
        IReadOnlyList<CapabilityDescriptor> tools, bool clarificationAllowed)
    {
        using var json = ClaudeMessagesRuntime.Parse(bytes);
        var root = json.RootElement;
        if (root.GetProperty("stop_reason").GetString() is "end_turn")
        {
            _ = ClaudeMessagesRuntime.ParseAnswer(bytes, profile, outputLimit, inputLimit);
            return;
        }
        ClaudeMessagesRuntime.ValidateMessage(root, profile, outputLimit, inputLimit, "tool_use");
        var content = root.GetProperty("content");
        if (content.ValueKind != JsonValueKind.Array || content.GetArrayLength() is < 1 or > 6
            || clarificationAllowed && content.GetArrayLength() != 1)
        { throw new InvalidDataException("Claude proposals exceed the admitted whole-batch bound."); }
        foreach (var block in content.EnumerateArray())
        {
            if (block.EnumerateObject().Count() != 4 || block.GetProperty("type").GetString() is not "tool_use"
                || block.GetProperty("id").ValueKind != JsonValueKind.String
                || block.GetProperty("input").ValueKind != JsonValueKind.Object)
            { throw new InvalidDataException("Only exact client tool proposals are admitted."); }
            var name = block.GetProperty("name").GetString();
            if (clarificationAllowed ? name is not ModelToolProtocol.AskUserToolName
                : !tools.Any(tool => string.Equals(Name(tool.Id), name, StringComparison.Ordinal)))
            { throw new InvalidDataException("Claude proposed a tool outside the advertised subset."); }
        }
    }

    internal static ModelAssistantMessage Parse(ChatResponse response, IReadOnlyList<CapabilityDescriptor> tools,
        bool clarificationAllowed)
    {
        if (response.Messages.Count != 1 || response.Messages[0].Role != ChatRole.Assistant)
        { throw new InvalidDataException("SDK returned an unsupported assistant message."); }
        var content = response.Messages[0].Contents;
        if (response.FinishReason == ChatFinishReason.Stop && content.All(item => item is TextContent))
        {
            return ModelToolProtocol.ParseAssistant(JsonSerializer.SerializeToUtf8Bytes(new
            { kind = "answer", answer = response.Text }));
        }
        if (response.FinishReason != ChatFinishReason.ToolCalls || content.Count is < 1 or > 6
            || content.Any(item => item is not FunctionCallContent))
        { throw new InvalidDataException("SDK returned unsupported tool proposals."); }
        var calls = content.OfType<FunctionCallContent>().ToArray();
        if (clarificationAllowed)
        {
            if (calls.Length != 1 || calls[0].Name is not ModelToolProtocol.AskUserToolName)
            { throw new InvalidDataException("A hosted clarification must be one isolated question."); }
            return ModelToolProtocol.ParseAskUser(JsonSerializer.SerializeToUtf8Bytes(ToolInput(calls[0])), calls[0].CallId);
        }
        return ModelToolProtocol.ParseAssistant(JsonSerializer.SerializeToUtf8Bytes(new
        {
            kind = "toolCalls",
            calls = calls.Select(call => new
            {
                callId = call.CallId,
                capabilityId = tools.SingleOrDefault(tool => string.Equals(Name(tool.Id), call.Name, StringComparison.Ordinal))?.Id
                    ?? throw new InvalidDataException("SDK proposed a tool outside the advertised subset."),
                input = ToolInput(call),
            }),
        }));
    }

    private static IReadOnlyDictionary<string, JsonElement> ToolInput(FunctionCallContent call)
    {
        if (call.RawRepresentation is not ToolUseBlock native
            || !string.Equals(native.ID, call.CallId, StringComparison.Ordinal)
            || !string.Equals(native.Name, call.Name, StringComparison.Ordinal))
        { throw new InvalidDataException("SDK tool projection lost the exact native proposal."); }
        return native.Input;
    }
}
