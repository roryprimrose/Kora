using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Application.Dependencies;

public sealed partial class ModelProviderHandoffWorkflow
{
    private const int MaximumPendingOffers = 16;
    private readonly Lock presentationGate = new();
    private readonly Dictionary<ModelHandoffOffer, Func<HostActivity>> pending = [];

    private async Task Publish(ModelHandoffResult result, CancellationToken token)
    {
        ModelHandoffOffer? rejected = null;
        lock (presentationGate)
        {
            foreach (var item in pending.Keys.Where(item => Volatile.Read(ref item.Used) != 0
                || Volatile.Read(ref item.Retired) != 0).ToArray())
            {
                pending.Remove(item);
            }
            if (result.Outcome is ModelHandoffOutcome.Offered or ModelHandoffOutcome.Removed)
            {
                var offer = result.Offer!;
                // Only a successfully audited host-issued reference enters this volatile source.
                if (pending.Count >= MaximumPendingOffers)
                {
                    rejected = offer;
                }
                else
                {
                    pending.Add(offer, HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Presentation));
                }
            }
        }
        if (rejected is not null)
        {
            Interlocked.Exchange(ref rejected.Retired, 1);
            var cancelled = await questions.CancelAsync(rejected.Question.Key, token).ConfigureAwait(false);
            throw new InvalidOperationException($"The bounded pending handoff presentation is full; question retirement outcome: {cancelled.Outcome}.");
        }
    }

    internal ModelHandoffOffer[] Pending()
    {
        lock (presentationGate) { return pending.Keys.ToArray(); }
    }

    internal Func<HostActivity>? Continuation(ModelHandoffOffer offer)
    {
        lock (presentationGate)
        {
            return pending.GetValueOrDefault(offer);
        }
    }

    internal async Task<ModelHandoffResult> Inspect(ModelHandoffOffer offer, CancellationToken token)
    {
        var observed = await host.CheckHandoffAsync(offer, token).ConfigureAwait(false);
        if (observed.Reason != ModelTurnReason.None) { return Denied(observed.Reason); }
        if (Volatile.Read(ref offer.Used) != 0) { return Denied(ModelTurnReason.HandoffReviewStale); }
        var reviewed = await reviews.ReviewAsync(offer.Question.Key, token).ConfigureAwait(false);
        if (reviewed.Outcome != HostInteractionOutcome.Presented) { return FromQuestion(reviewed); }
        var exact = reviewed.Question!;
        if (exact.SessionGeneration != offer.Generation || exact.ExpiresAt != offer.Context.ExpiresAt)
        {
            return Denied(ModelTurnReason.HandoffReviewStale);
        }
        observed = await host.CheckHandoffAsync(offer, token).ConfigureAwait(false);
        return observed.Reason == ModelTurnReason.None
            ? new(ModelHandoffOutcome.Offered, ModelTurnReason.None, offer) : Denied(observed.Reason);
    }

    internal void Retire(ModelHandoffOffer offer)
    {
        Interlocked.Exchange(ref offer.Retired, 1);
        lock (presentationGate)
        {
            pending.Remove(offer);
        }
    }
}
