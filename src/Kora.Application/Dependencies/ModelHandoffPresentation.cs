using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Dependencies;

/// <summary>A volatile original-user consumer of exact host-issued offers, never an adapter or egress authority.</summary>
public sealed class ModelHandoffPresentation(ModelProviderHandoffWorkflow workflow)
{
    public const string GateDisclosure = "Exact context review only. This offer has no admitted execution continuation. Approval is not runtime, account or final-request egress authority. This review sends and executes no hosted request.";
    public const string VerifiedDisclosure = "The exact local runtime returned Unavailable and its physical resources joined. Context approval creates one new original-session request, not permission to send. Separate final-byte, destination and account review is required; the original expiry is not renewed.";

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
        foreach (var offer in workflow.Pending()) { workflow.Revoke(offer); }
    }
}
