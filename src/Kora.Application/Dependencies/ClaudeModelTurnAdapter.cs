using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Dependencies;

internal sealed class ClaudeModelTurnAdapter(IClaudeTurnRuntime runtime, HostedTransmissionReviewService reviews,
    TimeProvider time, ILogger<BoundedModelTurnAdapter> logger) : IModelTurnAdapter
{
    private ClaudeTurnAuthorization? authorization;
    private Func<CancellationToken, ValueTask<bool>>? current;
    public bool IsQuiescent => runtime.IsQuiescent;
    internal bool Owns(ClaudeTurnAuthorization issued) => reviews.Current(issued);
    internal ClaudeModelTurnAdapter Bind(ClaudeTurnAuthorization issued, Func<CancellationToken, ValueTask<bool>> remainsCurrent) =>
        new(runtime, reviews, time, logger) { authorization = issued, current = remainsCurrent };

    public async Task<ModelProviderReply> RunAsync(ModelTurnProvenance provenance, ProviderProfileTuple? profile,
        ReadOnlyMemory<byte> context, Func<string, string, CancellationToken, ValueTask<CapabilityReply>> invokeTool,
        CancellationToken cancellationToken)
    {
        if (authorization is null || current is null || profile != authorization.Profile
            || !ReferenceEquals(provenance.Request, authorization.Context.Request)
            || !context.Span.SequenceEqual(authorization.Context.Serialize()) || !reviews.Current(authorization)
            || !await current(cancellationToken).ConfigureAwait(false))
        { return new(ModelTurnOutcome.DeniedEgress, provenance.Selection); }
        async ValueTask<bool> Live(CancellationToken token) =>
            reviews.Current(authorization) && await current(token).ConfigureAwait(false);
        ValueTask<bool> Review(ClaudeTransmission transmission, CancellationToken token) =>
            ReferenceEquals(transmission.Provenance, provenance) && transmission.Profile == profile
                ? reviews.ReviewAsync(authorization, transmission, Live, token) : ValueTask.FromResult(false);
        ModelProviderReply result;
        if (authorization.Context.ToolMode == ModelToolMode.ReadOnly)
        {
            if (runtime is not IClaudeToolTurnRuntime tools)
            { return new(ModelTurnOutcome.Unavailable, provenance.Selection); }
            var transport = await tools.OpenToolTurnAsync(provenance, profile, authorization.Context,
                Review, Live, cancellationToken).ConfigureAwait(false);
            if (transport is null) { return new(ModelTurnOutcome.Unavailable, provenance.Selection); }
            await using var loop = new BoundedModelTurnAdapter(transport, time, logger);
            result = await loop.RunAsync(provenance, profile, context, invokeTool,
                async token => await Live(token).ConfigureAwait(false) ? ModelTurnReason.None : ModelTurnReason.EgressNotAdmitted,
                cancellationToken).ConfigureAwait(false);
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
