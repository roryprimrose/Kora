using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Dependencies;

/// <summary>A volatile original-user consumer of exact host-issued offers, never an adapter or egress authority.</summary>
public sealed class ModelHandoffPresentation(ModelProviderHandoffWorkflow workflow)
{
    public const string GateDisclosure = "Exact context review only. Runtime qualification, account authority and final-request egress remain unavailable. Nothing was sent or executed.";

    public async Task<IReadOnlyList<ModelHandoffOffer>> ReadPending(RequestOrigin channel, Func<bool> eligible,
        CancellationToken token)
    {
        if (channel != RequestOrigin.LocalUi || !eligible()) { return []; }
        var admitted = new List<ModelHandoffOffer>();
        foreach (var offer in workflow.Pending())
        {
            token.ThrowIfCancellationRequested();
            var continuation = workflow.Continuation(offer);
            if (continuation is null) { continue; }
            using var activity = continuation();
            var result = await workflow.Inspect(offer, token).ConfigureAwait(false);
            if (!eligible()) { activity.Complete(HostOperationOutcome.Cancelled); return []; }
            if (result.Outcome == ModelHandoffOutcome.Offered) { admitted.Add(offer); }
            else { workflow.Retire(offer); }
            activity.Complete(result.Outcome == ModelHandoffOutcome.Offered ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
        }
        return admitted;
    }

    public ModelHandoffReviewSession? Open(ModelHandoffOffer offer, RequestOrigin channel, Func<bool> eligible)
    {
        if (channel != RequestOrigin.LocalUi || !eligible()) { return null; }
        var continuation = workflow.Continuation(offer);
        return continuation is null ? null : new(workflow, offer, continuation, eligible);
    }

    public void RevokeAll()
    {
        foreach (var offer in workflow.Pending()) { workflow.Retire(offer); }
    }
}
