using System.Text;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Dependencies;

/// <summary>Owns one exact review lifetime; closing retires content and suppresses all late callbacks.</summary>
public sealed class ModelHandoffReviewSession : IAsyncDisposable
{
    private readonly ModelProviderHandoffWorkflow workflow;
    private readonly Func<bool> eligible;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Lock gate = new();
    private Func<HostActivity> continuation;
    private ModelHandoffOffer? offer;
    private Task? operation;
    private bool closed;
    private bool busy;

    internal ModelHandoffReviewSession(ModelProviderHandoffWorkflow workflow, ModelHandoffOffer offer,
        Func<HostActivity> continuation, Func<bool> eligible)
    {
        this.workflow = workflow;
        this.offer = offer;
        this.continuation = continuation;
        this.eligible = eligible;
    }

    public ModelHandoffOffer? Offer { get { lock (gate) { return offer; } } }
    public string Preview { get { lock (gate) { return offer is { } exact ? Encoding.UTF8.GetString(exact.Context.Serialize()) : string.Empty; } } }
    public ModelHandoffOutcome Outcome { get; private set; } = ModelHandoffOutcome.Unavailable;
    public ModelTurnReason Reason { get; private set; } = ModelTurnReason.None;
    public bool CanReview { get { lock (gate) { return !closed && !busy && eligible(); } } }
    public bool HasReviewed { get; private set; }

    public Task Refresh(CancellationToken token) => Run((exact, admitted) => workflow.Inspect(exact, admitted), inspecting: true, markReviewed: true, dismissing: false, target: null, requireReview: false, token);

    public Task Validate(CancellationToken token) => Run((exact, admitted) => workflow.Inspect(exact, admitted), inspecting: true, markReviewed: false, dismissing: false, target: null, requireReview: false, token);

    public Task Decide(ModelHandoffOffer target, ModelHandoffDecision decision, RequestOrigin channel, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Run((exact, admitted) => workflow.ReviewAsync(exact, exact.Policy, exact.Context, decision, channel, admitted),
            inspecting: false, markReviewed: false, dismissing: false, target, requireReview: decision == ModelHandoffDecision.Approve, token);
    }

    public Task Remove(ModelHandoffOffer target, IReadOnlyCollection<HostId<EvidenceIdentity>> ids, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(target);
        return Run((exact, admitted) => workflow.RemoveAsync(exact, ids, admitted), inspecting: false,
            markReviewed: false, dismissing: false, target, requireReview: false, token);
    }

    public async Task Dismiss(CancellationToken token)
    {
        try
        {
            // Display closure may request cancellation, never bypass admission for an affirmative answer.
            await Run((exact, admitted) => workflow.ReviewAsync(exact, exact.Policy, exact.Context,
                ModelHandoffDecision.Cancel, RequestOrigin.LocalUi, admitted), inspecting: false,
                markReviewed: false, dismissing: true, target: null, requireReview: false, token).ConfigureAwait(false);
        }
        finally { Revoke(); }
    }

    private Task Run(Func<ModelHandoffOffer, CancellationToken, Task<ModelHandoffResult>> action, bool inspecting,
        bool markReviewed, bool dismissing, ModelHandoffOffer? target, bool requireReview, CancellationToken token)
    {
        lock (gate)
        {
            if (closed || busy) { return Task.CompletedTask; }
            if (target is not null && !ReferenceEquals(target, offer))
            {
                Outcome = ModelHandoffOutcome.Stale;
                Reason = ModelTurnReason.HandoffReviewStale;
                HasReviewed = false;
                return Task.CompletedTask;
            }
            if (requireReview && !HasReviewed) { return Task.CompletedTask; }
            if (!dismissing && !eligible())
            {
                Outcome = ModelHandoffOutcome.Stale;
                Reason = ModelTurnReason.HostAdmissionClosed;
                Revoke();
                return Task.CompletedTask;
            }
            busy = true;
            operation = Execute(action, inspecting, markReviewed, token);
            return operation;
        }
    }

    private async Task Execute(Func<ModelHandoffOffer, CancellationToken, Task<ModelHandoffResult>> action, bool inspecting,
        bool markReviewed, CancellationToken token)
    {
        var exact = offer!;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        using var activity = continuation();
        try
        {
            linked.Token.ThrowIfCancellationRequested();
            var result = await action(exact, linked.Token).ConfigureAwait(false);
            lock (gate)
            {
                if (closed || !eligible() || linked.IsCancellationRequested)
                {
                    if (result.Offer is { } late) { workflow.Retire(late); }
                    linked.Token.ThrowIfCancellationRequested();
                    Revoke();
                    activity.Complete(HostOperationOutcome.Cancelled);
                    return;
                }
                Outcome = result.Outcome;
                Reason = result.Reason;
                if (inspecting && result.Outcome == ModelHandoffOutcome.Offered) { HasReviewed |= markReviewed; }
                else if (result.Outcome == ModelHandoffOutcome.Removed)
                {
                    var replacement = result.Offer!;
                    workflow.Retire(exact);
                    offer = replacement;
                    continuation = workflow.Continuation(replacement)!;
                    HasReviewed = false;
                }
                else { Revoke(); }
            }
            activity.Complete(result.Outcome is ModelHandoffOutcome.Offered or ModelHandoffOutcome.Removed or ModelHandoffOutcome.Approved
                ? HostOperationOutcome.Completed : HostOperationOutcome.Failed);
        }
        catch (OperationCanceledException)
        {
            Outcome = ModelHandoffOutcome.Cancelled;
            Reason = ModelTurnReason.CallerCancelled;
            Revoke();
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            Revoke();
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
        finally { lock (gate) { busy = false; } }
    }

    public void Revoke()
    {
        lock (gate)
        {
            if (closed) { return; }
            closed = true;
            HasReviewed = false;
            workflow.Retire(offer!);
            offer = null;
        }
        lifetime.Cancel();
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Dismiss(CancellationToken.None).ConfigureAwait(false);
            Revoke();
            if (operation is { } pending) { await pending.ConfigureAwait(false); }
        }
        finally { Revoke(); lifetime.Dispose(); }
    }
}
