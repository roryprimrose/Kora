using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Dependencies;

/// <summary>Serial, volatile host-owned continuation. It cannot acquire tool or egress authority.</summary>
internal sealed partial class BoundedModelTurnAdapter(
    IModelToolTransport transport, TimeProvider time, ILogger<BoundedModelTurnAdapter> logger)
    : IModelTurnAdapter, IAsyncDisposable
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
        return RunOwnedAsync(provenance, profile, context, invokeTool, validateContinuation, released, cancellationToken);
    }

    private async Task<ModelProviderReply> RunOwnedAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ReadOnlyMemory<byte> wire, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation, TaskCompletionSource released,
        CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        try
        {
            if (!HasHostContext(provenance))
            { return Finish(provenance, ModelTurnOutcome.Denied, ModelTurnReason.HostContextRequired); }
            using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Runtime);
            try
            {
                var result = await ContinueAsync(provenance, profile, wire, invokeTool, validateContinuation,
                    linked.Token).ConfigureAwait(false);
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
        Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation, CancellationToken token)
    {
        ModelToolContext context;
        try { context = ModelToolProtocol.ReadContext(wire.Span, provenance); }
        catch (InvalidDataException)
        { return Finish(provenance, ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
        var history = new List<ModelToolMessage>();
        var callIds = new HashSet<string>(StringComparer.Ordinal);
        var inputBytes = 0;
        var outputBytes = 0;
        var calls = 0;
        var started = time.GetUtcNow();
        for (var round = 1; ; round++)
        {
            var denied = await CheckContinuationAsync(provenance, started, validateContinuation, token).ConfigureAwait(false);
            if (denied is not null) { return denied; }
            ModelToolRequest request;
            if (inputBytes >= ModelToolProtocol.MaximumInputUtf8Bytes || outputBytes >= ModelToolProtocol.MaximumOutputUtf8Bytes)
            { return Finish(provenance, ModelTurnOutcome.Denied, ModelTurnReason.ToolLoopBudgetExceeded); }
            try
            {
                request = ModelToolProtocol.CreateRequest(provenance, profile, context, history, round,
                    new(ModelToolProtocol.MaximumInputUtf8Bytes - inputBytes, ModelToolProtocol.MaximumOutputUtf8Bytes - outputBytes));
            }
            catch (InvalidDataException)
            { return Finish(provenance, ModelTurnOutcome.Denied, ModelTurnReason.ToolLoopBudgetExceeded); }
            var requestBytes = ModelToolProtocol.SerializeRequest(request).Length;
            ModelToolTransportReply reply;
            try { reply = await transport.CompleteAsync(request, token).ConfigureAwait(false); }
            catch (InvalidDataException)
            { return Finish(provenance, ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
            denied = await CheckContinuationAsync(provenance, started, validateContinuation, token).ConfigureAwait(false);
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
            // The closed Core hierarchy admits only a final answer or a fully validated call batch.
            var assistant = (ModelAssistantToolCalls)message;
            if (assistant.Calls.Length > ModelToolProtocol.MaximumCalls - calls)
            { return Finish(provenance, ModelTurnOutcome.Denied, ModelTurnReason.ToolLoopBudgetExceeded); }
            if (assistant.Calls.Any(call => callIds.Contains(call.CallId)))
            { return Finish(provenance, ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
            foreach (var call in assistant.Calls) { callIds.Add(call.CallId); }
            history.Add(assistant);
            foreach (var call in assistant.Calls)
            {
                denied = await CheckContinuationAsync(provenance, started, validateContinuation, token).ConfigureAwait(false);
                if (denied is not null) { return denied; }
                calls++;
                var toolReply = await invokeTool(call.CapabilityId, call.InputJson, token).ConfigureAwait(false);
                denied = await CheckContinuationAsync(provenance, started, validateContinuation, token).ConfigureAwait(false);
                if (denied is not null) { return denied; }
                if (toolReply is null)
                { return Finish(provenance, ModelTurnOutcome.Unknown, ModelTurnReason.ToolResultRejected); }
                if (toolReply.Outcome != CapabilityOutcome.Succeeded)
                {
                    return Finish(provenance, toolReply.Outcome == CapabilityOutcome.Denied
                        ? ModelTurnOutcome.Denied : ModelTurnOutcome.Unknown, ModelTurnReason.ToolResultRejected);
                }
                ModelToolResultMessage result;
                try { result = ModelToolProtocol.CreateToolResult(call, toolReply); }
                catch (InvalidDataException)
                { return Finish(provenance, ModelTurnOutcome.Unknown, ModelTurnReason.ToolResultRejected); }
                var resultBytes = ModelToolProtocol.SerializeMessage(result).Length;
                if (resultBytes > ModelToolProtocol.MaximumOutputUtf8Bytes - outputBytes)
                { return Finish(provenance, ModelTurnOutcome.Denied, ModelTurnReason.ToolLoopBudgetExceeded); }
                outputBytes += resultBytes;
                history.Add(result);
            }
        }
    }

    private async ValueTask<ModelProviderReply?> CheckContinuationAsync(ModelTurnProvenance provenance,
        DateTimeOffset started, Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation, CancellationToken token)
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
        return now < started || now - started >= ModelToolProtocol.Deadline
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
