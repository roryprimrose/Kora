using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Dependencies;

public sealed partial class ModelTurnHost
{
    private async Task<ModelTurnAdmission> AdmitConfiguredProfileAsync(ModelTurnChoice choice, ModelContextEnvelope context,
        ModelHandoffReview? review, CancellationToken token)
    {
        var current = HostActivity.Current;
        if (current is null || current.Activity!.IsStopped || current.Outcome != HostOperationOutcome.Unknown)
        { return new(Finish(ModelTurnOutcome.Denied, ModelTurnReason.HostContextRequired)); }
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Policy);
        var auditEvent = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "model.app-profile", SecurityAuditOutcome.Requested, SecurityAuditInitiator.System, current.Request.TaskId.Value.ToString("D"));
        audit.Write(auditEvent);
        try
        {
            var control = access.ControlRevision;
            bool Live() => Eligible(control) && !current.Activity!.IsStopped && current.Outcome == HostOperationOutcome.Unknown;
            var initialized = await InitializePolicyAsync(token).ConfigureAwait(false);
            if (initialized.Policy is not { } policy)
            {
                CompleteAudit(auditEvent, initialized.Result);
                activity.Complete(ToActivityOutcome(initialized.Result.Outcome));
                return new(initialized.Result);
            }
            var resolved = await providerProfileConfiguration!.ResolveTurnAsync(Live, token).ConfigureAwait(false);
            ModelTurnAdmission admission;
            if (resolved.Selection is null)
            { admission = new(Finish(ModelTurnOutcome.Unavailable, ModelTurnReason.UnregisteredModel)); }
            else
            {
                if (!Enum.IsDefined(choice) || choice == ModelTurnChoice.Local && resolved.Selection.Provider != ModelProviderIdentity.Ollama
                    || choice == ModelTurnChoice.Hosted && resolved.Selection.Provider == ModelProviderIdentity.Ollama
                    || policy.Mode == ModelProviderMode.LocalOnly && resolved.Selection.Provider != ModelProviderIdentity.Ollama)
                { admission = new(Finish(ModelTurnOutcome.Denied, ModelTurnReason.InvalidPolicy)); }
                else if (resolved.Selection.Provider != ModelProviderIdentity.Ollama && policy.Mode == ModelProviderMode.LocalFirst)
                {
                    // Review-only handoff is not app-profile or egress authority. Its independent exact dispatch remains gated.
                    admission = new(Finish(ModelTurnOutcome.Denied, review is null
                        ? ModelTurnReason.HandoffReviewRequired : ModelTurnReason.HandoffReviewStale));
                }
                else if (!resolved.Current()) { admission = new(Finish(ModelTurnOutcome.Denied, ModelTurnReason.PolicyChanged)); }
                else
                {
                    admission = await AdmitSelectionAsync(resolved.Selection, context, current, token).ConfigureAwait(false);
                    if (admission.Turn is { } turn)
                    {
                        turn.Policy = policy;
                        turn.Profile = resolved.Tuple;
                    }
                }
            }
            CompleteAudit(auditEvent, admission.Result);
            if (admission.Turn is not null && (!Live() || !resolved.Current() || token.IsCancellationRequested))
            {
                admission = new(Finish(token.IsCancellationRequested ? ModelTurnOutcome.Cancelled : ModelTurnOutcome.Denied,
                    token.IsCancellationRequested ? ModelTurnReason.CallerCancelled : ModelTurnReason.PolicyChanged));
                CompleteAudit(auditEvent, admission.Result);
            }
            activity.Complete(ToActivityOutcome(admission.Result.Outcome));
            return admission;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            var result = Finish(ModelTurnOutcome.Cancelled, ModelTurnReason.CallerCancelled);
            CompleteAudit(auditEvent, result);
            activity.Complete(HostOperationOutcome.Cancelled);
            return new(result);
        }
        catch (Exception exception)
        {
            Fault(logger, exception.GetType().Name);
            CompleteAudit(auditEvent, new(ModelTurnOutcome.Unknown, ModelTurnReason.ProviderOutcome));
            activity.Complete(HostOperationOutcome.Unknown);
            throw;
        }
    }
}
