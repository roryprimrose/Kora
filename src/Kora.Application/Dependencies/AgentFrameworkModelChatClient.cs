using System.Text.Json;
using System.Text;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Microsoft.Extensions.AI;
using static Kora.Core.Dependencies.ModelToolChatMessages;

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
    private readonly Func<ModelClarificationQuestion, CancellationToken, Task<ModelQuestionAnswerMessage>>? ask;
    private readonly Action<Exception>? questionFault;
    private readonly QuestionFunction? questionFunction;
    private readonly string questionCorrelation = Guid.NewGuid().ToString("N");
    private readonly int inputLimit;
    private readonly int outputLimit;
    private readonly int callLimit;
    private ModelClarificationQuestion? pendingQuestion;
    private bool questionAsked;
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
        Action beginRound, Action<ModelTokenUsage?> observeTokens, Action<int, int, int, int> progress,
        Func<ModelClarificationQuestion, CancellationToken, Task<ModelQuestionAnswerMessage>>? ask = null,
        Action<Exception>? questionFault = null)
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
        this.ask = ask;
        this.questionFault = questionFault;
        inputLimit = context.Constraints?.InputUtf8Bytes ?? ModelToolProtocol.MaximumInputUtf8Bytes;
        outputLimit = context.Constraints?.OutputUtf8Bytes ?? ModelToolProtocol.MaximumOutputUtf8Bytes;
        callLimit = context.WebPages?.MaximumCalls ?? context.Constraints?.MaximumCalls ?? ModelToolProtocol.MaximumCalls;
        MaximumRounds = context.WebPages?.MaximumRounds ?? context.Constraints?.MaximumRounds ?? ModelToolProtocol.MaximumRounds;
        Tools = context.Tools.Select(descriptor => (AITool)new HostFunction(this, descriptor)).ToArray();
        if (context.McpTool is { } external)
        { Tools = [new HostFunction(this, external.Id, JsonDocument.Parse(external.InputSchema))]; }
        if (ask is not null) { questionFunction = new(this); }
    }

    internal IList<AITool> Tools { get; }
    internal int MaximumRounds { get; }
    internal IList<AITool> QuestionTools => questionFunction is null ? [] : [questionFunction];

    public async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
        => await GetResponseCoreAsync(messages, options, null, cancellationToken).ConfigureAwait(false);

    private async Task<ChatResponse> GetResponseCoreAsync(IEnumerable<ChatMessage> messages, ChatOptions? options,
        Func<ModelAnswerUpdate, CancellationToken, ValueTask>? publish, CancellationToken cancellationToken)
    {
        await CheckAsync(cancellationToken).ConfigureAwait(false);
        var transcript = messages.ToArray();
        ValidateTranscript(transcript);
        if (final is not null || pending is not null || pendingQuestion is not null
            || options?.Tools is not { } tools || tools.Count != Tools.Count
            || tools.Where((tool, index) => !ReferenceEquals(tool, Tools[index])).Any())
        { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
        if (++round > MaximumRounds || inputBytes >= inputLimit || outputBytes >= outputLimit)
        { throw Stop(ModelTurnOutcome.Denied, ModelTurnReason.ToolLoopBudgetExceeded); }
        ModelToolRequest request;
        try
        {
            request = ModelToolProtocol.CreateRequest(provenance, profile, context, history, round,
                new(inputLimit - inputBytes, outputLimit - outputBytes), clarificationAllowed: ask is not null,
                chatMessages: transcript);
        }
        catch (InvalidDataException)
        { throw Stop(ModelTurnOutcome.Denied, ModelTurnReason.ToolLoopBudgetExceeded); }
        var requestBytes = ModelToolProtocol.SerializeRequest(request).Length;
        beginRound();
        ModelToolTransportReply reply;
        try
        {
            reply = publish is not null && transport is IModelAnswerTransport streaming
                ? await streaming.CompleteWithAnswerAsync(request, publish, cancellationToken).ConfigureAwait(false)
                : await transport.CompleteAsync(request, cancellationToken).ConfigureAwait(false);
        }
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
        var message = reply.Message!;
        try
        {
            _ = ModelToolProtocol.ParseAssistant(ModelToolProtocol.SerializeMessage(message),
                clarificationAllowed: ask is not null, mcpTool: context.McpTool,
                formsAllowed: ModelToolProtocol.AllowsForms(context.ToolMode, provenance.Selection.Provider));
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
        if (message is ModelClarificationQuestion question)
        {
            if (ask is null || questionAsked
                || context.ToolMode == ModelToolMode.InteractiveConversation && question.CallId is null
                || question.Question.Fields is not null
                    && (!ModelToolProtocol.AllowsForms(context.ToolMode, provenance.Selection.Provider) || question.CallId is null)
                || question.CallId is { } questionId && !callIds.Add(questionId)
                || context.ToolMode == ModelToolMode.ReadOnly && question.CallId is null && question.Question.Options.Count == 0)
            { throw Stop(ModelTurnOutcome.Denied, ModelTurnReason.InvalidProviderResponse); }
            questionAsked = true;
            pendingQuestion = question;
            history.Add(question);
            var call = QuestionCall(question);
            return new(new ChatMessage(ChatRole.Assistant, question.CallId is null
                ? [new TextContent(Text(question)), call] : [call])) { FinishReason = ChatFinishReason.ToolCalls };
        }
        var batch = (ModelAssistantToolCalls)message;
        if (context.McpTool is not null && calls + batch.Calls.Length > 1
            || context.McpTool is null && batch.Calls.Any(call => !context.Tools.Any(tool =>
                string.Equals(tool.Id, call.CapabilityId, StringComparison.Ordinal))))
        { throw Stop(ModelTurnOutcome.Denied, ModelTurnReason.ToolUseNotPermitted); }
        if (context.ToolMode == ModelToolMode.LocalWebPage
            && (batch.Calls.Length != 1 || calls >= (context.WebPages?.MaximumCalls ?? 1))
            || batch.Calls.Length > callLimit - calls)
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
        if (pending is null || invocation is null || nextCall >= pending.Calls.Length)
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
        if (reply.WebPage is { } page && page.Request != provenance.Request)
        { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.ToolResultRejected); }
        ModelToolResultMessage result;
        try { result = ModelToolProtocol.CreateToolResult(call, reply, context.McpTool); }
        catch (InvalidDataException)
        { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.ToolResultRejected); }
        var bytes = ModelToolProtocol.SerializeMessage(result).Length;
        if (bytes > outputLimit - outputBytes)
        { throw Stop(ModelTurnOutcome.Denied, ModelTurnReason.ToolLoopBudgetExceeded); }
        outputBytes += bytes;
        history.Add(result);
        if (++nextCall == pending.Calls.Length) { pending = null; }
        return result;
    }

    private FunctionCallContent QuestionCall(ModelClarificationQuestion question) =>
        new(question.CallId ?? questionCorrelation, ModelToolProtocol.AskUserToolName,
            Arguments(Encoding.UTF8.GetString(ModelToolProtocol.SerializeQuestionArguments(question.Question))));

    private async ValueTask<object?> InvokeQuestionAsync(AIFunction function, AIFunctionArguments arguments, CancellationToken token)
    {
        await CheckAsync(token).ConfigureAwait(false);
        var invocation = FunctionInvokingChatClient.CurrentContext;
        if (pendingQuestion is not { } question || ask is null || invocation is null
            || !ReferenceEquals(invocation.Function, function)
            || invocation.FunctionCount != 1 || invocation.FunctionCallIndex != 0
            || !MatchesQuestion(question, invocation.CallContent)
            || !SameArguments(Encoding.UTF8.GetString(ModelToolProtocol.SerializeQuestionArguments(question.Question)), arguments))
        { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
        ModelQuestionAnswerMessage answer;
        try { answer = await ask(question, token).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException
            or IOException or Kora.Core.Conversation.ConversationCredentialException)
        {
            questionFault?.Invoke(exception);
            throw Stop(ModelTurnOutcome.Denied, ModelTurnReason.HostAdmissionClosed);
        }
        await CheckAsync(token).ConfigureAwait(false);
        if (answer is null || !ReferenceEquals(answer.Question, question))
        { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
        var bytes = ModelToolProtocol.SerializeMessage(answer).Length;
        if (bytes > outputLimit - outputBytes)
        { throw Stop(ModelTurnOutcome.Denied, ModelTurnReason.ToolLoopBudgetExceeded); }
        outputBytes += bytes;
        history.Add(answer);
        pendingQuestion = null;
        return answer;
    }

    private bool MatchesQuestion(ModelClarificationQuestion question, FunctionCallContent function) =>
        ModelToolChatMessages.MatchesQuestion(question, function, questionCorrelation);

    internal string ReadFinalAnswer(IList<ChatMessage> messages)
    {
        if (pending is not null || pendingQuestion is not null || final is null || messages.LastOrDefault() is not { Role: var role } last
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
        try { ModelToolChatMessages.Validate(context, history, messages, questionCorrelation); }
        catch (InvalidDataException)
        { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
    }

    internal async ValueTask ValidatePublicationAsync(CancellationToken token)
    {
        if (await checkContinuation(token).ConfigureAwait(false) is not null)
        { throw new InvalidDataException("Provisional answer admission changed."); }
        token.ThrowIfCancellationRequested();
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        if (ask is not null || context.ToolMode != ModelToolMode.None
            || provenance.Selection.Provider != ModelProviderIdentity.Ollama || transport is not IModelAnswerTransport)
        { throw new NotSupportedException("Only the admitted local tool-free answer supports streaming."); }
        return StreamAsync(messages, options, cancellationToken);
    }

    private async IAsyncEnumerable<ChatResponseUpdate> StreamAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var closed = false;
        var channel = Channel.CreateBounded<StreamItem>(new BoundedChannelOptions(1)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
        var producer = ProduceAsync();
        try
        {
            await foreach (var item in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return item.Update;
                item.Accepted.TrySetResult();
            }
            await producer.ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref closed, true);
            await lifetime.CancelAsync().ConfigureAwait(false);
            try { await producer.ConfigureAwait(false); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { /* Enumeration disposal cancels publication but still joins the physical transport. */ }
        }

        async Task SendAsync(ChatResponseUpdate update)
        {
            var item = new StreamItem(update);
            await channel.Writer.WriteAsync(item, lifetime.Token).ConfigureAwait(false);
            await item.Accepted.Task.WaitAsync(lifetime.Token).ConfigureAwait(false);
        }

        async Task ProduceAsync()
        {
            Exception? failure = null;
            var previous = string.Empty;
            try
            {
                var response = await GetResponseCoreAsync(messages, options, async (update, updateToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (Volatile.Read(ref closed))
                    { throw new InvalidDataException("The provisional answer stream has closed."); }
                    update.Validate();
                    if (update.Provenance != provenance || !update.Answer.StartsWith(previous, StringComparison.Ordinal)
                        || Encoding.UTF8.GetByteCount(update.Answer) > outputLimit - outputBytes)
                    { throw new InvalidDataException("Provisional answer no longer matches its exact admitted prefix and budget."); }
                    await ValidatePublicationAsync(cancellationToken).ConfigureAwait(false);
                    updateToken.ThrowIfCancellationRequested();
                    if (update.Answer.Length != previous.Length)
                    {
                        await SendAsync(new(ChatRole.Assistant, update.Answer[previous.Length..])
                        { MessageId = "answer" }).ConfigureAwait(false);
                        previous = update.Answer;
                    }
                }, lifetime.Token).ConfigureAwait(false);
                if (final is null || !final.Answer.StartsWith(previous, StringComparison.Ordinal))
                { throw Stop(ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
                var terminal = response.ToChatResponseUpdates();
                foreach (var update in terminal)
                {
                    update.MessageId = "answer";
                    foreach (var content in update.Contents.OfType<TextContent>())
                    { content.Text = final.Answer[previous.Length..]; previous = final.Answer; }
                    await SendAsync(update).ConfigureAwait(false);
                }
            }
            catch (Exception exception)
            { failure = exception; throw; }
            finally { channel.Writer.TryComplete(failure); }
        }
    }

    private sealed class StreamItem(ChatResponseUpdate update)
    {
        internal ChatResponseUpdate Update { get; } = update;
        internal TaskCompletionSource Accepted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

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
        private readonly string name;
        private readonly JsonElement schema;

        internal HostFunction(AgentFrameworkModelChatClient owner, CapabilityDescriptor descriptor)
        {
            this.owner = owner;
            name = descriptor.Id;
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream)) { CapabilityInputSchema.Write(writer, descriptor); }
            using var document = JsonDocument.Parse(stream.ToArray());
            schema = document.RootElement.Clone();
        }

        internal HostFunction(AgentFrameworkModelChatClient owner, string name, JsonDocument document)
        {
            this.owner = owner;
            this.name = name;
            using (document) { schema = document.RootElement.Clone(); }
        }

        public override string Name => name;
        public override JsonElement JsonSchema => schema;
        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) =>
            owner.InvokeAsync(this, arguments, cancellationToken);
    }

    private sealed class QuestionFunction(AgentFrameworkModelChatClient owner) : AIFunction
    {
        public override string Name => ModelToolProtocol.AskUserToolName;
        protected override ValueTask<object?> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken) =>
            owner.InvokeQuestionAsync(this, arguments, cancellationToken);
    }
}
