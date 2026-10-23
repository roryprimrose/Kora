using Kora.Core.Dependencies;
using Kora.Core.Providers;
using Kora.Core.Tools;

namespace Kora.Application.Dependencies;

internal sealed class CopilotModelTurnAdapter(ICopilotTurnRuntime runtime, HostedTransmissionReviewService reviews) : IModelTurnAdapter
{
    private CopilotTurnAuthorization? authorization;
    private Func<CancellationToken, ValueTask<bool>>? current;
    public bool IsQuiescent => runtime.IsQuiescent;

    internal bool Owns(CopilotTurnAuthorization issued) => reviews.Current(issued);

    internal CopilotModelTurnAdapter Bind(CopilotTurnAuthorization issued, Func<CancellationToken, ValueTask<bool>> remainsCurrent) =>
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
        var reply = await runtime.RunAsync(provenance, profile, context, async (id, input, token) =>
            {
                if (!reviews.Current(authorization) || !await current(token).ConfigureAwait(false))
                { return new(CapabilityOutcome.Denied, "copilot-turn-not-current"); }
                var response = await invokeTool(id, input, token).ConfigureAwait(false);
                return await current(token).ConfigureAwait(false) ? response : new(CapabilityOutcome.Denied, "copilot-turn-changed");
            },
            (transmission, token) => ReferenceEquals(transmission.Provenance, provenance) && transmission.Profile == profile
                ? reviews.ReviewAsync(authorization, transmission, current, token) : ValueTask.FromResult(false),
            cancellationToken).ConfigureAwait(false);
        return new(reply.Outcome, provenance.Selection,
            reply.Outcome == ModelTurnOutcome.Succeeded ? new LocalModelResponse(reply.Answer, null) : null);
    }
}
