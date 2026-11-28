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
        var tokens = new TokenTotals();
        try
        {
            if (!HasHostContext(provenance))
            { return Finish(provenance, ModelTurnOutcome.Denied, ModelTurnReason.HostContextRequired); }
            using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Runtime);
            try
            {
                var result = await ContinueAsync(provenance, profile, wire, invokeTool, validateContinuation, ask,
                    publish, tokens, linked.Token).ConfigureAwait(false);
                activity.Complete(result.Outcome == ModelTurnOutcome.Succeeded ? HostOperationOutcome.Completed
                    : result.Outcome == ModelTurnOutcome.Cancelled ? HostOperationOutcome.Cancelled
                    : result.Outcome == ModelTurnOutcome.Unknown ? HostOperationOutcome.Unknown : HostOperationOutcome.Failed);
                return result with { TokenUsage = tokens.Snapshot };
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            { activity.Complete(HostOperationOutcome.Cancelled); throw; }
            catch
            { activity.Complete(HostOperationOutcome.Unknown); throw; }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        { return Finish(provenance, ModelTurnOutcome.Cancelled, ModelTurnReason.CallerCancelled) with { TokenUsage = tokens.Snapshot }; }
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
        Func<ModelAnswerUpdate, CancellationToken, ValueTask>? publish, TokenTotals tokens, CancellationToken token)
    {
        ModelToolContext context;
        try { context = ModelToolProtocol.ReadContext(wire.Span, provenance); }
        catch (InvalidDataException)
        { return Finish(provenance, ModelTurnOutcome.Unknown, ModelTurnReason.InvalidProviderResponse); }
        if (ask is not null && !ModelToolProtocol.AllowsClarification(context.ToolMode, provenance.Selection.Provider))
        { return Finish(provenance, ModelTurnOutcome.Denied, ModelTurnReason.ToolUseNotPermitted); }
        return await RunAgentFrameworkAsync(provenance, profile, context, invokeTool,
            validateContinuation, ask, publish, tokens, token).ConfigureAwait(false);
    }

    private async ValueTask<ModelProviderReply?> CheckContinuationAsync(ModelTurnProvenance provenance,
        DateTimeOffset started, Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation,
        TimeSpan deadline, ModelTurnLifetime? turnLifetime, CancellationToken token)
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
        return now < started || now >= (turnLifetime?.Extend(started + deadline) ?? started + deadline)
            ? Finish(provenance, ModelTurnOutcome.TimedOut, ModelTurnReason.DeadlineExceeded) : null;
    }

    private static bool HasHostContext(ModelTurnProvenance provenance) =>
        HostActivity.Current is { Outcome: HostOperationOutcome.Unknown, Activity.IsStopped: false } current
        && ReferenceEquals(current.Request, provenance.Request);

    private sealed class TokenTotals
    {
        private long? input = 0;
        private long? output = 0;
        private ModelTokenUsage? latest;
        private bool pending;
        private bool observed;

        internal ModelTokenUsage? Snapshot => observed
            ? new(pending ? null : input, pending ? null : output, latest?.ContextTokens, latest?.ContextCapacityTokens)
            : null;

        internal void BeginRound()
        {
            pending = true;
            latest = null;
        }

        internal void Observe(ModelTokenUsage? usage)
        {
            usage?.Validate();
            try
            {
                var nextInput = checked(input + usage?.InputTokens);
                var nextOutput = checked(output + usage?.OutputTokens);
                input = nextInput;
                output = nextOutput;
            }
            catch (OverflowException exception)
            { throw new InvalidDataException("Provider token totals exceed the supported count range.", exception); }
            latest = usage;
            observed |= usage is not null;
            pending = false;
        }
    }

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
