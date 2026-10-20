using System.Diagnostics;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Network;
using Kora.Tools.Network;

namespace Kora.Application.Network;

/// <summary>Consumes shared durable local admission; source and serialization policy stay in the web action.</summary>
public sealed class WebSummaryModelExecution(
    IModelContextRunner turns, WebResultSummarize action, TimeProvider time,
    IWebResultModelUsePolicy sourcePolicy) : IWebSummaryModelExecution
{
    public async Task<ModelTurnResult> Run(WebSummaryReview review, Func<bool> current, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(review);
        var confirmation = HostActivity.RequireCurrent();
        if (confirmation.Request.Origin != review.Target.Request.Origin)
        { return new(ModelTurnOutcome.Denied, ModelTurnReason.OriginalUserRequired); }
        using var request = HostActivity.BeginRoot(review.Target.SourceRequest, HostActivityLayer.Application, HostOperation.Request,
            [new ActivityLink(review.Trace), new ActivityLink(confirmation.Activity!.Context)]);
        try
        {
            token.ThrowIfCancellationRequested();
            action.RequireCredentialScreenAccepted(review.Snapshot);
            var policyDecision = (int)WebResultModelUseDecision.Allowed;
            WebResultModelUseDecision CurrentPolicy() => (WebResultModelUseDecision)Volatile.Read(ref policyDecision);
            bool Eligible() => current() && CurrentPolicy() == WebResultModelUseDecision.Allowed;
            async Task ValidatePolicy(CancellationToken validationToken)
            {
                var decision = await sourcePolicy.AdmitAsync(review.Snapshot, validationToken).ConfigureAwait(false);
                validationToken.ThrowIfCancellationRequested();
                if (decision != WebResultModelUseDecision.Allowed)
                {
                    // A denied/unknown observation closes this use permanently, even if a later check would allow it.
                    var closed = decision == WebResultModelUseDecision.Denied ? decision : WebResultModelUseDecision.Unavailable;
                    Interlocked.CompareExchange(ref policyDecision, (int)closed, (int)WebResultModelUseDecision.Allowed);
                }
            }
            ModelTurnResult PolicyFailure() => new(CurrentPolicy() == WebResultModelUseDecision.Denied
                ? ModelTurnOutcome.Denied : ModelTurnOutcome.Unavailable, ModelTurnReason.LocalEvidenceNotDisclosable);
            await ValidatePolicy(token).ConfigureAwait(false);
            if (!Eligible())
            {
                request.Complete(HostOperationOutcome.Failed);
                return CurrentPolicy() != WebResultModelUseDecision.Allowed ? PolicyFailure()
                    : new(ModelTurnOutcome.Denied, ModelTurnReason.ContextExpired);
            }
            var admission = await turns.AdmitAsync(review.Target, Eligible, token, ValidatePolicy).ConfigureAwait(false);
            LocalContextTurnReceipt receipt;
            if (admission.Turn is { } admitted)
            {
                await using (admitted.ConfigureAwait(false))
                {
                    token.ThrowIfCancellationRequested();
                    await ValidatePolicy(token).ConfigureAwait(false);
                    if (!Eligible())
                    {
                        receipt = new(review.Target.Request, ModelTurnOutcome.Denied, LocalContextTurnReason.SourceNotCurrent);
                    }
                    else
                    {
                        if (!ReferenceEquals(admitted.Request, review.Target.Request) || admitted.SessionGeneration.Value <= 0)
                        { throw new InvalidDataException("Local web context admission changed the exact reviewed session/task target."); }
                        var context = action.CreateContext(review.Snapshot, admitted.Request, time.GetUtcNow(), review.ExpiresAt);
                        receipt = await admitted.RunAsync(context, token).ConfigureAwait(false);
                    }
                }
            }
            else
            {
                // The closed admission type contains exactly a lease or a typed rejection.
                receipt = admission.Rejection!;
            }
            if (!token.IsCancellationRequested) { await ValidatePolicy(token).ConfigureAwait(false); }
            if (CurrentPolicy() != WebResultModelUseDecision.Allowed)
            {
                request.Complete(HostOperationOutcome.Failed);
                return PolicyFailure();
            }
            var result = receipt.ModelResult is { } model && model.Outcome == receipt.Outcome
                ? model
                : new ModelTurnResult(receipt.Outcome == ModelTurnOutcome.Succeeded ? ModelTurnOutcome.Unknown : receipt.Outcome,
                    receipt.Reason switch
                    {
                        LocalContextTurnReason.OriginalInputRequired => ModelTurnReason.OriginalUserRequired,
                        LocalContextTurnReason.TargetAlreadyUsed => ModelTurnReason.TurnAlreadyUsed,
                        LocalContextTurnReason.ContextMismatch => ModelTurnReason.ContextMismatch,
                        LocalContextTurnReason.SourceNotCurrent => ModelTurnReason.ContextExpired,
                        LocalContextTurnReason.SessionNotCurrent => ModelTurnReason.SessionNotAdmitted,
                        LocalContextTurnReason.Cancelled => ModelTurnReason.CallerCancelled,
                        LocalContextTurnReason.ModelUnavailable => ModelTurnReason.UnregisteredModel,
                        LocalContextTurnReason.EvidenceUnavailable => ModelTurnReason.HostInterrupted,
                        _ => ModelTurnReason.InvalidProviderResponse,
                    });
            request.Complete(result.Outcome == ModelTurnOutcome.Succeeded ? HostOperationOutcome.Completed
                : result.Outcome == ModelTurnOutcome.Cancelled ? HostOperationOutcome.Cancelled : HostOperationOutcome.Failed);
            return result;
        }
        catch (OperationCanceledException)
        {
            request.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch
        {
            request.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }
}
