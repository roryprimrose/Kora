using System.Text.Json;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Microsoft.Extensions.AI;

namespace Kora.Application.Dependencies;

/// <summary>Translates one volatile package session without delegating host authority or transport ownership.</summary>
internal sealed class AgentFrameworkModelChatClient : IChatClient
{
    private readonly IModelToolTransport transport;
    private readonly ModelTurnProvenance provenance;
    private readonly ProviderProfileTuple? profile;
    private readonly ModelToolContext context;
    private readonly Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool;
    private readonly Func<CancellationToken, ValueTask<ModelProviderReply?>> checkContinuation;
    private readonly Action beginRound;
    private readonly Action<ModelTokenUsage?> observeTokens;
    private readonly Action<int, int, int, int> progress;
    private readonly List<ModelToolMessage> history = [];
    private readonly HashSet<string> callIds = new(StringComparer.Ordinal);
    private readonly ModelTurnConstraints constraints;
    private ModelAssistantToolCalls? pending;
    private ModelFinalResponse? final;
    private int nextCall;
    private int calls;
    private int round;
    private int inputBytes;
    private int outputBytes;

    internal AgentFrameworkModelChatClient(IModelToolTransport transport, ModelTurnProvenance provenance,
        ProviderProfileTuple? profile, ModelToolContext context,
        Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        Func<CancellationToken, ValueTask<ModelProviderReply?>> checkContinuation,
        Action beginRound, Action<ModelTokenUsage?> observeTokens, Action<int, int, int, int> progress)
    {
        this.transport = transport;
        this.provenance = provenance;
        this.profile = profile;
        this.context = context;
        this.invokeTool = invokeTool;
        this.checkContinuation = checkContinuation;
        this.beginRound = beginRound;
        this.observeTokens = observeTokens;
        this.progress = progress;
        constraints = context.Constraints!;
        Tools = context.Tools.Select(descriptor => (AITool)new HostFunction(this, descriptor)).ToArray();
    }

    internal IList<AITool> Tools { get; }

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        await CheckAsync(cancellationToken).ConfigureAwait(false);
        ValidateTranscript(messages);
        if (final is not null || pending is not null
            || options?.Tools is not { } tools || tools.Count != Tools.Count
            || tools.Where((tool, index) => !ReferenceEquals(tool, Tools[index])).Any())
        { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
        if (++round > constraints.MaximumRounds || inputBytes >= constraints.InputUtf8Bytes
            || outputBytes >= constraints.OutputUtf8Bytes)
        { throw Stop(ModelTurnOutcome.Denied, ModelTurnReason.ToolLoopBudgetExceeded); }
        ModelToolRequest request;
        try
        {
            request = ModelToolProtocol.CreateRequest(provenance, profile, context, history, round,
                new(constraints.InputUtf8Bytes - inputBytes, constraints.OutputUtf8Bytes - outputBytes));
        }
        catch (InvalidDataException)
        { throw Stop(ModelTurnOutcome.Denied, ModelTurnReason.ToolLoopBudgetExceeded); }
        var requestBytes = ModelToolProtocol.SerializeRequest(request).Length;
        beginRound();
        ModelToolTransportReply reply;
        try { reply = await transport.CompleteAsync(request, cancellationToken).ConfigureAwait(false); }
        catch (InvalidDataException)
        { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
        if (reply is null || reply.Provenance != provenance || !Enum.IsDefined(reply.Outcome)
            || (reply.Outcome == ModelTurnOutcome.Succeeded) != (reply.Message is not null))
        { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
        try { observeTokens(reply.TokenUsage); }
        catch (InvalidDataException)
        { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
        await CheckAsync(cancellationToken).ConfigureAwait(false);
        if (reply.Outcome != ModelTurnOutcome.Succeeded)
        { throw Stop(reply.Outcome, ModelTurnReason.ProviderOutcome); }
        ModelAssistantMessage message;
        try
        {
            message = ModelToolProtocol.ParseAssistant(ModelToolProtocol.SerializeMessage(reply.Message!));
        }
        catch (InvalidDataException)
        { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
        var messageBytes = ModelToolProtocol.SerializeMessage(message).Length;
        if (reply.Usage is not { } usage || usage.InputUtf8Bytes < requestBytes || usage.OutputUtf8Bytes < messageBytes
            || usage.InputUtf8Bytes > request.Budget.InputUtf8Bytes || usage.OutputUtf8Bytes > request.Budget.OutputUtf8Bytes)
        { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
        inputBytes += usage.InputUtf8Bytes;
        outputBytes += usage.OutputUtf8Bytes;
        progress(round, calls, inputBytes, outputBytes);
        if (message is ModelFinalResponse answer)
        {
            final = answer;
            return new(new ChatMessage(ChatRole.Assistant, answer.Answer)) { FinishReason = ChatFinishReason.Stop };
        }
        var batch = (ModelAssistantToolCalls)message;
        if (batch.Calls.Any(call => !context.Tools.Any(tool =>
            string.Equals(tool.Id, call.CapabilityId, StringComparison.Ordinal))))
        { throw Stop(ModelTurnOutcome.Denied, ModelTurnReason.ToolUseNotPermitted); }
        if (batch.Calls.Length > constraints.MaximumCalls - calls)
        { throw Stop(ModelTurnOutcome.Denied, ModelTurnReason.ToolLoopBudgetExceeded); }
        if (batch.Calls.Any(call => callIds.Contains(call.CallId)))
        { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
        foreach (var call in batch.Calls) { callIds.Add(call.CallId); }
        pending = batch;
        nextCall = 0;
        history.Add(batch);
        return new(new ChatMessage(ChatRole.Assistant, batch.Calls.Select(call => (AIContent)
            new FunctionCallContent(call.CallId, call.CapabilityId, Arguments(call.InputJson))).ToList()))
        { FinishReason = ChatFinishReason.ToolCalls };
    }

    private async ValueTask<object?> InvokeAsync(HostFunction function, AIFunctionArguments arguments,
        CancellationToken token)
    {
        await CheckAsync(token).ConfigureAwait(false);
        var invocation = FunctionInvokingChatClient.CurrentContext;
        if (pending is null || invocation is null)
        { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
        var call = pending.Calls[nextCall];
        if (!ReferenceEquals(invocation.Function, function)
            || !string.Equals(call.CapabilityId, function.Name, StringComparison.Ordinal)
            || !Matches(call, invocation.CallContent) || !SameArguments(call.InputJson, arguments))
        { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
        calls++;
        var reply = await invokeTool(call.CapabilityId, call.InputJson, token).ConfigureAwait(false);
        await CheckAsync(token).ConfigureAwait(false);
        if (reply is null)
        { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.ToolResultRejected); }
        if (reply.Outcome != CapabilityOutcome.Succeeded)
        {
            throw Stop(reply.Outcome switch
            {
                CapabilityOutcome.Denied => ModelTurnOutcome.Denied,
                CapabilityOutcome.Unavailable => ModelTurnOutcome.Unavailable,
                _ => ModelTurnOutcome.Unknown,
            }, ModelTurnReason.ToolResultRejected);
        }
        ModelToolResultMessage result;
        try { result = ModelToolProtocol.CreateToolResult(call, reply); }
        catch (InvalidDataException)
        { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.ToolResultRejected); }
        var bytes = ModelToolProtocol.SerializeMessage(result).Length;
        if (bytes > constraints.OutputUtf8Bytes - outputBytes)
        { throw Stop(ModelTurnOutcome.Denied, ModelTurnReason.ToolLoopBudgetExceeded); }
        outputBytes += bytes;
        history.Add(result);
        if (++nextCall == pending.Calls.Length) { pending = null; }
        return result;
    }

    internal string ReadFinalAnswer(IList<ChatMessage> messages)
    {
        if (pending is not null || final is null || messages.LastOrDefault() is not { Role: var role } last
            || role != ChatRole.Assistant || last.Contents.Count != 1
            || last.Contents[0] is not TextContent text || !string.Equals(text.Text, final.Answer, StringComparison.Ordinal))
        { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
        ValidateTranscript(messages.Take(messages.Count - 1).Prepend(new(ChatRole.User, context.UserRequest)));
        return final.Answer;
    }

    private async ValueTask CheckAsync(CancellationToken token)
    {
        var denied = await checkContinuation(token).ConfigureAwait(false);
        if (denied is not null) { throw Stop(denied.Outcome, denied.Reason); }
    }

    private void ValidateTranscript(IEnumerable<ChatMessage> messages)
    {
        var contents = messages.SelectMany(message => message.Contents.Select(content => (message.Role, Content: content))).ToArray();
        if (contents.Length == 0 || contents[0].Role != ChatRole.User || contents[0].Content is not TextContent original
            || !string.Equals(original.Text, context.UserRequest, StringComparison.Ordinal))
        { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
        var index = 1;
        foreach (var item in history)
        {
            if (item is ModelAssistantToolCalls batch)
            {
                foreach (var call in batch.Calls)
                {
                    if (index >= contents.Length || contents[index].Role != ChatRole.Assistant
                        || contents[index++].Content is not FunctionCallContent function || !Matches(call, function))
                    { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
                }
            }
            else
            {
                var result = (ModelToolResultMessage)item;
                if (index >= contents.Length || contents[index].Role != ChatRole.Tool
                    || contents[index++].Content is not FunctionResultContent function
                    || !string.Equals(function.CallId, result.CallId, StringComparison.Ordinal)
                    || !ReferenceEquals(function.Result, result) || function.Exception is not null)
                { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
            }
        }
        if (index != contents.Length)
        { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
    }

    private static bool Matches(ModelToolCall call, FunctionCallContent function) =>
        string.Equals(function.CallId, call.CallId, StringComparison.Ordinal)
        && string.Equals(function.Name, call.CapabilityId, StringComparison.Ordinal)
        && SameArguments(call.InputJson, function.Arguments);

    private static bool SameArguments(string input, IEnumerable<KeyValuePair<string, object?>>? arguments)
    {
        using var expected = JsonDocument.Parse(input);
        return JsonElement.DeepEquals(expected.RootElement,
            JsonSerializer.SerializeToElement(arguments?.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal)));
    }

    private static Dictionary<string, object?> Arguments(string input)
    {
        using var document = JsonDocument.Parse(input);
        return document.RootElement.EnumerateObject().ToDictionary(property => property.Name,
            property => (object?)property.Value.Clone(), StringComparer.Ordinal);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Bounded agents require complete, validated model batches.");

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose() => GC.SuppressFinalize(this);

    private static TurnStoppedException Stop(ModelTurnOutcome outcome, ModelTurnReason reason) => new(outcome, reason);

    internal sealed class TurnStoppedException(ModelTurnOutcome outcome, ModelTurnReason reason)
        : Exception("The host stopped the bounded agent operation.")
    {
        internal ModelTurnOutcome Outcome { get; } = outcome;
        internal ModelTurnReason Reason { get; } = reason;
    }

    private sealed class HostFunction : AIFunction
    {
        private readonly AgentFrameworkModelChatClient owner;
        private readonly CapabilityDescriptor descriptor;
        private readonly JsonElement schema;

        internal HostFunction(AgentFrameworkModelChatClient owner, CapabilityDescriptor descriptor)
        {
            this.owner = owner;
            this.descriptor = descriptor;
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream)) { CapabilityInputSchema.Write(writer, descriptor); }
            using var document = JsonDocument.Parse(stream.ToArray());
            schema = document.RootElement.Clone();
        }

        public override string Name => descriptor.Id;
        public override JsonElement JsonSchema => schema;
        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) =>
            owner.InvokeAsync(this, arguments, cancellationToken);
    }
}
