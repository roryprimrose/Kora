using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Dependencies;

internal sealed class ClaudeModelTurnAdapter(IClaudeTurnRuntime runtime, HostedTransmissionReviewService reviews,
    TimeProvider time, ILogger<BoundedModelTurnAdapter> logger) : IModelTurnAdapter, IModelAnswerTurnAdapter, IModelQuestionTurnAdapter
{
    private ClaudeTurnAuthorization? authorization;
    private Func<CancellationToken, ValueTask<bool>>? current;
    public bool IsQuiescent => runtime.IsQuiescent;
    internal bool Owns(ClaudeTurnAuthorization issued) => reviews.Current(issued);
    internal ClaudeModelTurnAdapter Bind(ClaudeTurnAuthorization issued, Func<CancellationToken, ValueTask<bool>> remainsCurrent) =>
        new(runtime, reviews, time, logger) { authorization = issued, current = remainsCurrent };
    internal bool StreamsAnswer => authorization?.StreamAnswer == true;

    public Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        CancellationToken cancellationToken) =>
        RunCoreAsync(provenance, profile, context, invokeTool, null, cancellationToken);

    public Task<ModelProviderReply> RunWithAnswerAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation,
        Func<ModelAnswerUpdate, CancellationToken, ValueTask> publish, CancellationToken cancellationToken) =>
        RunCoreAsync(provenance, profile, context, invokeTool, async (update, token) =>
        {
            update.Validate();
            if (update.Provenance != provenance
                || await validateContinuation(token).ConfigureAwait(false) != ModelTurnReason.None)
            { throw new InvalidDataException("Claude provisional host admission changed."); }
            await publish(update, token).ConfigureAwait(false);
        }, cancellationToken);

    public Task<ModelProviderReply> RunWithQuestionAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        Func<CancellationToken, ValueTask<ModelTurnReason>> validateContinuation,
        Func<ModelClarificationQuestion, CancellationToken, Task<ModelQuestionAnswerMessage>> ask,
        CancellationToken cancellationToken) =>
        RunCoreAsync(provenance, profile, context, invokeTool, null, cancellationToken, async (question, token) =>
        {
            if (await validateContinuation(token).ConfigureAwait(false) != ModelTurnReason.None)
            { throw new InvalidOperationException("Claude question admission changed."); }
            var answer = await ask(question, token).ConfigureAwait(false);
            if (await validateContinuation(token).ConfigureAwait(false) != ModelTurnReason.None)
            { throw new InvalidOperationException("Claude answer admission changed."); }
            return answer;
        });

    private async Task<ModelProviderReply> RunCoreAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        Func<ModelAnswerUpdate, CancellationToken, ValueTask>? publish, CancellationToken cancellationToken,
        Func<ModelClarificationQuestion, CancellationToken, Task<ModelQuestionAnswerMessage>>? ask = null)
    {
        if (authorization is null || current is null || profile != authorization.Profile
            || !ReferenceEquals(provenance.Request, authorization.Context.Request)
            || !context.Span.SequenceEqual(authorization.Context.Serialize()) || !reviews.Current(authorization)
            || !await current(cancellationToken).ConfigureAwait(false))
        { return new(ModelTurnOutcome.DeniedEgress, provenance.Selection); }
        if ((authorization.Context.ToolMode == ModelToolMode.HostedClarification) != (ask is not null))
        { return new(ModelTurnOutcome.Denied, provenance.Selection, Reason: ModelTurnReason.ToolUseNotPermitted); }
        async ValueTask<bool> Live(CancellationToken token) =>
            reviews.Current(authorization) && await current(token).ConfigureAwait(false);
        ValueTask<bool> Review(ClaudeTransmission transmission, CancellationToken token) =>
            ReferenceEquals(transmission.Provenance, provenance) && transmission.Profile == profile
                ? reviews.ReviewAsync(authorization, transmission, Live, token) : ValueTask.FromResult(false);
        ModelProviderReply result;
        if (authorization.StreamAnswer)
        {
            if (publish is null || runtime is not IClaudeAnswerTurnRuntime streaming)
            { return new(ModelTurnOutcome.Unavailable, provenance.Selection); }
            var reply = await streaming.RunWithAnswerAsync(provenance, profile, authorization.Context,
                Review, Live, async (update, token) =>
                {
                    if (!await Live(token).ConfigureAwait(false))
                    { throw new InvalidDataException("Claude provisional account/source authority changed."); }
                    await publish(update, token).ConfigureAwait(false);
                }, cancellationToken).ConfigureAwait(false);
            result = new(reply.Outcome, provenance.Selection,
                reply.Outcome == ModelTurnOutcome.Succeeded ? new LocalModelResponse(reply.Answer, null) : null);
        }
        else if (authorization.Context.ToolMode is ModelToolMode.ReadOnly or ModelToolMode.HostedClarification)
        {
            if (runtime is not IClaudeToolTurnRuntime tools)
            { return new(ModelTurnOutcome.Unavailable, provenance.Selection); }
            var transport = await tools.OpenToolTurnAsync(provenance, profile, authorization.Context,
                Review, Live, cancellationToken).ConfigureAwait(false);
            if (transport is null) { return new(ModelTurnOutcome.Unavailable, provenance.Selection); }
            await using var loop = new BoundedModelTurnAdapter(transport, time, logger);
            async ValueTask<ModelTurnReason> Validate(CancellationToken token) =>
                await Live(token).ConfigureAwait(false) ? ModelTurnReason.None : ModelTurnReason.EgressNotAdmitted;
            result = ask is null
                ? await loop.RunAsync(provenance, profile, context, invokeTool, Validate, cancellationToken).ConfigureAwait(false)
                : await loop.RunWithQuestionAsync(provenance, profile, context, invokeTool, Validate, ask, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var reply = await runtime.RunAsync(provenance, profile, authorization.Context,
                Review, Live, cancellationToken).ConfigureAwait(false);
            result = new(reply.Outcome, provenance.Selection,
                reply.Outcome == ModelTurnOutcome.Succeeded ? new LocalModelResponse(reply.Answer, null) : null);
        }
        if (result.Outcome == ModelTurnOutcome.Succeeded && !await Live(cancellationToken).ConfigureAwait(false))
        { return new(ModelTurnOutcome.DeniedEgress, provenance.Selection); }
        return result;
    }
}
