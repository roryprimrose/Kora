using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Application.Dependencies;

internal sealed class ClaudeModelTurnAdapter(IClaudeTurnRuntime runtime, HostedTransmissionReviewService reviews) : IModelTurnAdapter
{
    private ClaudeTurnAuthorization? authorization;
    private Func<CancellationToken, ValueTask<bool>>? current;
    public bool IsQuiescent => runtime.IsQuiescent;
    internal bool Owns(ClaudeTurnAuthorization issued) => reviews.Current(issued);
    internal ClaudeModelTurnAdapter Bind(ClaudeTurnAuthorization issued, Func<CancellationToken, ValueTask<bool>> remainsCurrent) =>
        new(runtime, reviews) { authorization = issued, current = remainsCurrent };

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
        var reply = await runtime.RunAsync(provenance, profile, authorization.Context,
            (transmission, token) => ReferenceEquals(transmission.Provenance, provenance) && transmission.Profile == profile
                ? reviews.ReviewAsync(authorization, transmission, Live, token) : ValueTask.FromResult(false),
            Live, cancellationToken).ConfigureAwait(false);
        if (reply.Outcome == ModelTurnOutcome.Succeeded && !await Live(cancellationToken).ConfigureAwait(false))
        { return new(ModelTurnOutcome.DeniedEgress, provenance.Selection); }
        return new(reply.Outcome, provenance.Selection,
            reply.Outcome == ModelTurnOutcome.Succeeded ? new LocalModelResponse(reply.Answer, null) : null);
    }
}
