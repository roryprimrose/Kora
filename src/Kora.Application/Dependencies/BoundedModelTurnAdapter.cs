using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Dependencies;

/// <summary>Serial, volatile host-owned continuation. It cannot acquire tool or egress authority.</summary>
internal sealed partial class BoundedModelTurnAdapter(
    IModelToolTransport transport, TimeProvider time, ILogger<BoundedModelTurnAdapter> logger)
    : IModelTurnAdapter, IModelQuestionTurnAdapter, IModelAnswerTurnAdapter, IAsyncDisposable
{
    private readonly Lock gate = new();
    private readonly CancellationTokenSource lifetime = new();
    private TaskCompletionSource? running;
    private bool disposed;
    private Task? disposal;

    internal bool IsQuiescent { get { lock (gate) { return running is null || running.Task.IsCompleted; } } }

    public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        return Task.FromResult(Finish(provenance, ModelTurnOutcome.Denied, ModelTurnReason.HostContextRequired));
    }

    public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation, CancellationToken cancellationToken)
        => StartAsync(provenance, profile, context, invokeTool, validateContinuation, null, cancellationToken);

    public Task<ModelProviderReply> RunWithQuestionAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation,
        Func<ModelClarificationQuestion, CancellationToken, Task<ModelQuestionAnswerMessage>> ask,
        CancellationToken cancellationToken) =>
        StartAsync(provenance, profile, context, invokeTool, validateContinuation, ask, cancellationToken);

    public Task<ModelProviderReply> RunWithAnswerAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation,
        Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish, CancellationToken cancellationToken) =>
        StartAsync(provenance, profile, context, invokeTool, validateContinuation, null, cancellationToken, publish);

    private Task<ModelProviderReply> StartAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation,
        Func<ModelClarificationQuestion, CancellationToken, Task<ModelQuestionAnswerMessage>>? ask,
        CancellationToken cancellationToken, Func<ModelAnswerUpdate, CancellationToken, ValueTask>? publish = null)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        ArgumentNullException.ThrowIfNull(invokeTool);
        ArgumentNullException.ThrowIfNull(validateContinuation);
        TaskCompletionSource released;
        lock (gate)
        {
            if (disposed)
            { return Task.FromResult(Finish(provenance, ModelTurnOutcome.Unavailable, ModelTurnReason.HostAdmissionClosed)); }
            if (!IsQuiescent)
            { return Task.FromResult(Finish(provenance, ModelTurnOutcome.Unavailable, ModelTurnReason.ProviderBusy)); }
            running = released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        return RunOwnedAsync(provenance, profile, context, invokeTool, validateContinuation, ask, released, publish, cancellationToken);
    }

    private async Task<ModelProviderReply> RunOwnedAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ReadOnlyMemory<byte> wire, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation,
        Func<ModelClarificationQuestion, CancellationToken, Task<ModelQuestionAnswerMessage>>? ask, TaskCompletionSource released,
        Func<ModelAnswerUpdate, CancellationToken, ValueTask>? publish, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        try
        {
            if (!HasHostContext(provenance))
            { return Finish(provenance, ModelTurnOutcome.Denied, ModelTurnReason.HostContextRequired); }
            using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Runtime);
            try
            {
                var result = await ContinueAsync(provenance, profile, wire, invokeTool, validateContinuation, ask,
                    publish, linked.Token).ConfigureAwait(false);
                activity.Complete(result.Outcome == ModelTurnOutcome.Succeeded ? HostOperationOutcome.Completed
                    : result.Outcome == ModelTurnOutcome.Cancelled ? HostOperationOutcome.Cancelled
                    : result.Outcome == ModelTurnOutcome.Unknown ? HostOperationOutcome.Unknown : HostOperationOutcome.Failed);
                return result;
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            { activity.Complete(HostOperationOutcome.Cancelled); throw; }
            catch
            { activity.Complete(HostOperationOutcome.Unknown); throw; }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        { return Finish(provenance, ModelTurnOutcome.Cancelled, ModelTurnReason.CallerCancelled); }
        catch (Exception exception)
        {
            Fault(logger, exception.GetType().Name);
            throw;
        }
        finally { released.TrySetResult(); }
    }

    private async Task<ModelProviderReply> ContinueAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ReadOnlyMemory<byte> wire, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation,
        Func<ModelClarificationQuestion, CancellationToken, Task<ModelQuestionAnswerMessage>>? ask,
        Func<ModelAnswerUpdate, CancellationToken, ValueTask>? publish, CancellationToken token)
    {
        ModelToolContext context;
        try { context = ModelToolProtocol.ReadContext(wire.Span, provenance); }
        catch (InvalidDataException)
        { return Finish(provenance, ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
        if (ask is not null && !ModelToolProtocol.AllowsClarification(context.ToolMode, provenance.Selection.Provider))
        { return Finish(provenance, ModelTurnOutcome.Denied, ModelTurnReason.ToolUseNotPermitted); }
        var history = new List<ModelToolMessage>();
        var questionAsked = false;
        var callIds = new HashSet<string>(StringComparer.Ordinal);
        var inputBytes = 0;
        var outputBytes = 0;
        var calls = 0;
        var started = time.GetUtcNow();
        var deadline = context.ToolMode == ModelToolMode.HostedClarification
            ? HostedTurnAuthorization.MaximumDuration : ModelToolProtocol.Deadline;
        var inputLimit = context.Constraints?.InputUtf8Bytes ?? ModelToolProtocol.MaximumInputUtf8Bytes;
        var outputLimit = context.Constraints?.OutputUtf8Bytes ?? ModelToolProtocol.MaximumOutputUtf8Bytes;
        var callLimit = context.WebPages?.MaximumCalls ?? context.Constraints?.MaximumCalls ?? ModelToolProtocol.MaximumCalls;
        for (var round = 1; ; round++)
        {
            var denied = await CheckContinuationAsync(provenance, started, validateContinuation, deadline, token).ConfigureAwait(false);
            if (denied is not null) { return denied; }
            ModelToolRequest request;
            if (inputBytes >= inputLimit || outputBytes >= outputLimit
                || round > (context.WebPages?.MaximumRounds ?? context.Constraints?.MaximumRounds ?? ModelToolProtocol.MaximumRounds))
            { return Finish(provenance, ModelTurnOutcome.Denied, ModelTurnReason.ToolLoopBudgetExceeded); }
            try
            {
                request = ModelToolProtocol.CreateRequest(provenance, profile, context, history, round,
                    new(inputLimit - inputBytes, outputLimit - outputBytes),
                    clarificationAllowed: ask is not null);
            }
            catch (InvalidDataException)
            { return Finish(provenance, ModelTurnOutcome.Denied, ModelTurnReason.ToolLoopBudgetExceeded); }
            var requestBytes = ModelToolProtocol.SerializeRequest(request).Length;
            ModelToolTransportReply reply;
            try
            {
                reply = publish is not null && ask is null && context.ToolMode == ModelToolMode.None
                    && provenance.Selection.Provider == ModelProviderIdentity.Ollama && transport is IModelAnswerTransport streaming
                    ? await streaming.CompleteWithAnswerAsync(request, async (update, updateToken) =>
                    {
                        update.Validate();
                        if (update.Provenance != provenance
                            || await CheckContinuationAsync(provenance, started, validateContinuation, deadline, token).ConfigureAwait(false) is not null)
                        { throw new InvalidDataException("Provisional answer admission changed."); }
                        updateToken.ThrowIfCancellationRequested();
                        await publish(update, token).ConfigureAwait(false);
                    }, token).ConfigureAwait(false)
                    : await transport.CompleteAsync(request, token).ConfigureAwait(false);
            }
            catch (InvalidDataException)
            { return Finish(provenance, ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
            denied = await CheckContinuationAsync(provenance, started, validateContinuation, deadline, token).ConfigureAwait(false);
            if (denied is not null) { return denied; }
            if (reply is null || reply.Provenance != provenance || !Enum.IsDefined(reply.Outcome)
                || (reply.Outcome == ModelTurnOutcome.Succeeded) != (reply.Message is not null))
            { return Finish(provenance, ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
            if (reply.Outcome != ModelTurnOutcome.Succeeded)
            { return Finish(provenance, reply.Outcome, ModelTurnReason.ProviderOutcome); }
            var message = reply.Message!;
            var messageBytes = ModelToolProtocol.SerializeMessage(message).Length;
            if (reply.Usage is not { } usage || usage.InputUtf8Bytes < requestBytes || usage.OutputUtf8Bytes < messageBytes
                || usage.InputUtf8Bytes > request.Budget.InputUtf8Bytes || usage.OutputUtf8Bytes > request.Budget.OutputUtf8Bytes)
            { return Finish(provenance, ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
            inputBytes += usage.InputUtf8Bytes;
            outputBytes += usage.OutputUtf8Bytes;
            Progress(logger, round, calls, inputBytes, outputBytes);
            if (message is ModelFinalResponse answer)
            { return Finish(provenance, ModelTurnOutcome.Succeeded, ModelTurnReason.None, new(answer.Answer, null)); }
            if (message is ModelClarificationQuestion question)
            {
                if (ask is null || questionAsked
                    || question.Question.Fields is not null
                        && (!ModelToolProtocol.AllowsForms(context.ToolMode, provenance.Selection.Provider) || question.CallId is null)
                    || question.CallId is { } questionId && !callIds.Add(questionId)
                    || context.ToolMode == ModelToolMode.ReadOnly && question.CallId is null && question.Question.Options.Count == 0)
                { return Finish(provenance, ModelTurnOutcome.Denied, ModelTurnReason.InvalidProviderResponse); }
                questionAsked = true;
                ModelQuestionAnswerMessage replyAnswer;
                try { replyAnswer = await ask(question, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { throw; }
                catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException
                    or IOException or Kora.Core.Conversation.ConversationCredentialException)
                {
                    Fault(logger, exception.GetType().Name);
                    return Finish(provenance, ModelTurnOutcome.Denied, ModelTurnReason.HostAdmissionClosed);
                }
                denied = await CheckContinuationAsync(provenance, started, validateContinuation, deadline, token).ConfigureAwait(false);
                if (denied is not null) { return denied; }
                if (replyAnswer is null || !ReferenceEquals(replyAnswer.Question, question))
                { return Finish(provenance, ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
                var answerBytes = ModelToolProtocol.SerializeMessage(replyAnswer).Length;
                if (answerBytes > outputLimit - outputBytes)
                { return Finish(provenance, ModelTurnOutcome.Denied, ModelTurnReason.ToolLoopBudgetExceeded); }
                outputBytes += answerBytes;
                history.Add(question);
                history.Add(replyAnswer);
                continue;
            }
            // Remaining assistant messages are fully validated tool-call batches.
            var assistant = (ModelAssistantToolCalls)message;
            try
            {
                _ = ModelToolProtocol.ParseAssistant(ModelToolProtocol.SerializeMessage(assistant), mcpTool: context.McpTool);
                if (context.McpTool is not null && calls + assistant.Calls.Length > 1
                    || context.McpTool is null && assistant.Calls.Any(call =>
                        !context.Tools.Any(tool =>
                            string.Equals(tool.Id, call.CapabilityId, StringComparison.Ordinal))))
                { return Finish(provenance, ModelTurnOutcome.Denied, ModelTurnReason.ToolUseNotPermitted); }
            }
            catch (InvalidDataException)
            { return Finish(provenance, ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
            if (context.ToolMode == ModelToolMode.LocalWebPage
                && (assistant.Calls.Length != 1 || calls >= (context.WebPages?.MaximumCalls ?? 1)))
            { return Finish(provenance, ModelTurnOutcome.Denied, ModelTurnReason.ToolLoopBudgetExceeded); }
            if (assistant.Calls.Length > callLimit - calls)
            { return Finish(provenance, ModelTurnOutcome.Denied, ModelTurnReason.ToolLoopBudgetExceeded); }
            if (assistant.Calls.Any(call => callIds.Contains(call.CallId)))
            { return Finish(provenance, ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
            foreach (var call in assistant.Calls) { callIds.Add(call.CallId); }
            history.Add(assistant);
            foreach (var call in assistant.Calls)
            {
                denied = await CheckContinuationAsync(provenance, started, validateContinuation, deadline, token).ConfigureAwait(false);
                if (denied is not null) { return denied; }
                calls++;
                var toolReply = await invokeTool(call.CapabilityId, call.InputJson, token).ConfigureAwait(false);
                denied = await CheckContinuationAsync(provenance, started, validateContinuation, deadline, token).ConfigureAwait(false);
                if (denied is not null) { return denied; }
                if (toolReply is null)
                { return Finish(provenance, ModelTurnOutcome.Unknown, ModelTurnReason.ToolResultRejected); }
                if (toolReply.Outcome != CapabilityOutcome.Succeeded)
                {
                    return Finish(provenance, toolReply.Outcome == CapabilityOutcome.Denied
                        ? ModelTurnOutcome.Denied : ModelTurnOutcome.Unknown, ModelTurnReason.ToolResultRejected);
                }
                if (toolReply.WebPage is { } page && page.Request != provenance.Request)
                { return Finish(provenance, ModelTurnOutcome.Unknown, ModelTurnReason.ToolResultRejected); }
                ModelToolResultMessage result;
                try { result = ModelToolProtocol.CreateToolResult(call, toolReply, context.McpTool); }
                catch (InvalidDataException)
                { return Finish(provenance, ModelTurnOutcome.Unknown, ModelTurnReason.ToolResultRejected); }
                var resultBytes = ModelToolProtocol.SerializeMessage(result).Length;
                if (resultBytes > outputLimit - outputBytes)
                { return Finish(provenance, ModelTurnOutcome.Denied, ModelTurnReason.ToolLoopBudgetExceeded); }
                outputBytes += resultBytes;
                history.Add(result);
            }
        }
    }

    private async ValueTask<ModelProviderReply?> CheckContinuationAsync(ModelTurnProvenance provenance,
        DateTimeOffset started, Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation,
        TimeSpan deadline, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!HasHostContext(provenance))
        { return Finish(provenance, ModelTurnOutcome.Denied, ModelTurnReason.HostContextRequired); }
        var reason = await validateContinuation(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (reason != ModelTurnReason.None)
        { return Finish(provenance, ModelTurnOutcome.Denied, Enum.IsDefined(reason) ? reason : ModelTurnReason.HostAdmissionClosed); }
        if (!HasHostContext(provenance))
        { return Finish(provenance, ModelTurnOutcome.Denied, ModelTurnReason.HostContextRequired); }
        var now = time.GetUtcNow();
        return now < started || now - started >= deadline
            ? Finish(provenance, ModelTurnOutcome.TimedOut, ModelTurnReason.DeadlineExceeded) : null;
    }

    private static bool HasHostContext(ModelTurnProvenance provenance) =>
        HostActivity.Current is { Outcome: HostOperationOutcome.Unknown, Activity.IsStopped: false } current
        && ReferenceEquals(current.Request, provenance.Request);

    private ModelProviderReply Finish(ModelTurnProvenance provenance, ModelTurnOutcome outcome, ModelTurnReason reason,
        LocalModelResponse? response = null)
    {
        Result(logger, outcome, reason);
        return new(outcome, provenance.Selection, response, reason);
    }

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        lock (gate) { return new(disposal ??= CloseAsync()); }
    }

    private async Task CloseAsync()
    {
        disposed = true;
        await lifetime.CancelAsync().ConfigureAwait(false);
        Task outstanding;
        lock (gate) { outstanding = running?.Task ?? Task.CompletedTask; }
        await outstanding.ConfigureAwait(false);
        try { await transport.DisposeAsync().ConfigureAwait(false); }
        finally { lifetime.Dispose(); }
    }
}
