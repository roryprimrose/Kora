using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Application.Dependencies;

internal sealed class CopilotModelTurnAdapter(ICopilotTurnRuntime runtime, HostedTransmissionReviewService reviews)
    : IModelTurnAdapter, IModelAnswerTurnAdapter
{
    private CopilotTurnAuthorization? authorization;
    private Func<CancellationToken, ValueTask<bool>>? current;
    public bool IsQuiescent => runtime.IsQuiescent;

    internal bool Owns(CopilotTurnAuthorization issued) => reviews.Current(issued);

    internal CopilotModelTurnAdapter Bind(CopilotTurnAuthorization issued, Func<CancellationToken, ValueTask<bool>> remainsCurrent) =>
        new(runtime, reviews) { authorization = issued, current = remainsCurrent };

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
            { throw new InvalidDataException("Copilot provisional host admission changed."); }
            await publish(update, token).ConfigureAwait(false);
        }, cancellationToken);

    private async Task<ModelProviderReply> RunCoreAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        Func<ModelAnswerUpdate, CancellationToken, ValueTask>? publish, CancellationToken cancellationToken)
    {
        async ValueTask<bool> IsCurrent(CancellationToken token) =>
            authorization is not null && current is not null && reviews.Current(authorization)
            && await reviews.PermissionCurrentAsync(authorization, token).ConfigureAwait(false)
            && await current(token).ConfigureAwait(false)
            && (authorization.Selection is null || authorization.SourceCurrent is not null
                && await authorization.SourceCurrent(token).ConfigureAwait(false)
                && await current(token).ConfigureAwait(false) && reviews.Current(authorization));
        if (authorization is null || current is null || profile != authorization.Profile
            || !ReferenceEquals(provenance.Request, authorization.Context.Request)
            || !context.Span.SequenceEqual(authorization.Context.Serialize()) || !reviews.Current(authorization)
            || !await IsCurrent(cancellationToken).ConfigureAwait(false))
        { return new(ModelTurnOutcome.DeniedEgress, provenance.Selection); }
        if (authorization.Selection is not null && runtime is not ICopilotCustomizationTurnRuntime)
        { return new(ModelTurnOutcome.Unavailable, provenance.Selection, Reason: ModelTurnReason.UnregisteredModel); }
        async ValueTask<CapabilityReply> Invoke(string id, string input, CancellationToken token)
        {
            if (!await IsCurrent(token).ConfigureAwait(false))
            { return new(CapabilityOutcome.Denied, "copilot-turn-not-current"); }
            var response = await invokeTool(id, input, token).ConfigureAwait(false);
            return await IsCurrent(token).ConfigureAwait(false) ? response : new(CapabilityOutcome.Denied, "copilot-turn-changed");
        }
        ValueTask<bool> Review(CopilotTransmission transmission, CancellationToken token) =>
            ReferenceEquals(transmission.Provenance, provenance) && transmission.Profile == profile
                ? reviews.ReviewAsync(authorization, transmission, IsCurrent, token) : ValueTask.FromResult(false);
        CopilotTurnReply reply;
        if (authorization.Selection is { } selected && runtime is ICopilotCustomizationTurnRuntime native)
        {
            reply = await native.RunSelectedAsync(provenance, profile, context, selected, IsCurrent, Review,
                cancellationToken).ConfigureAwait(false);
        }
        else if (publish is not null && runtime is ICopilotAnswerTurnRuntime streaming)
        {
            if (authorization.Context.ToolMode != ModelToolMode.None)
            { return new(ModelTurnOutcome.Denied, provenance.Selection, Reason: ModelTurnReason.ToolUseNotPermitted); }
            reply = await streaming.RunWithAnswerAsync(provenance, profile, context, Invoke, Review, async (update, token) =>
            {
                if (!await IsCurrent(token).ConfigureAwait(false))
                { throw new InvalidDataException("Copilot provisional account/source authority changed."); }
                await publish(update, token).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            reply = await runtime.RunAsync(provenance, profile, context, Invoke, Review, cancellationToken).ConfigureAwait(false);
        }
        if (reply.Outcome == ModelTurnOutcome.Succeeded
            && !await IsCurrent(cancellationToken).ConfigureAwait(false))
        { return new(ModelTurnOutcome.DeniedEgress, provenance.Selection) { TokenUsage = reply.TokenUsage }; }
        return new(reply.Outcome, provenance.Selection,
            reply.Outcome == ModelTurnOutcome.Succeeded ? new LocalModelResponse(reply.Answer, null) : null)
        { TokenUsage = reply.TokenUsage };
    }
}
