using Kora.Application.Dependencies;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

namespace Kora.Application.Definitions;

/// <summary>Separate fresh-request admission; preparation never uses the legacy local reasoner.</summary>
public sealed class PromptRequestConsumer(
    InstructionControlAdmission admission, OwnedInstructionEditorActions sources,
    ModelTurnHost models, ISecurityAuditLog audit, TimeProvider time)
{
    public Task<ModelTurnResult> Use(ExpandedPromptRequest prepared, Func<bool> eligible, CancellationToken token) =>
        admission.Run(eligible, async (request, session, current, admitted) =>
        {
            using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Policy);
            var requested = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ProtectedOperation,
                "definition.prompt.request", SecurityAuditOutcome.Requested, SecurityAuditInitiator.LocalUser, "definition.owned-prompt");
            audit.Write(requested);
            try
            {
                await admission.Publish(request, session, current, static () => true, admitted).ConfigureAwait(false);
                var sourceCurrent = await sources.IsCurrentAdmitted(prepared.Source, current, admitted).ConfigureAwait(false);
                admitted.ThrowIfCancellationRequested();
                if (!sourceCurrent)
                { throw new InvalidOperationException("The exact reviewed prompt source changed before request admission."); }
                var now = time.GetUtcNow();
                var context = prepared.CreateContext(request, now);
                // The actual complete JSON payload, including host tools/policy overhead, is bounded at admission.
                var result = (await models.AdmitAsync(ModelTurnChoice.Default, context, null, admitted).ConfigureAwait(false)).Result;
                sourceCurrent = await sources.IsCurrentAdmitted(prepared.Source, current, admitted).ConfigureAwait(false);
                admitted.ThrowIfCancellationRequested();
                if (!sourceCurrent)
                { throw new InvalidOperationException("The exact reviewed prompt source changed during request admission."); }
                await admission.Publish(request, session, current, static () => true, admitted).ConfigureAwait(false);
                audit.Write(requested.WithOutcome(SecurityAuditOutcome.Failed,
                    result.Outcome == ModelTurnOutcome.Succeeded ? "generation-not-enabled" : "model-admission-unavailable"));
                activity.Complete(HostOperationOutcome.Failed);
                // Even a controlled qualified admission does not enable production generation in this increment.
                return result.Outcome == ModelTurnOutcome.Succeeded
                    ? new ModelTurnResult(ModelTurnOutcome.Unavailable, ModelTurnReason.GenerationNotEnabled, result.Provenance)
                    : result;
            }
            catch (OperationCanceledException)
            {
                audit.Write(requested.WithOutcome(SecurityAuditOutcome.Cancelled, "cancelled"));
                activity.Complete(HostOperationOutcome.Cancelled);
                throw;
            }
            catch
            {
                audit.Write(requested.WithOutcome(SecurityAuditOutcome.Failed, "request-unconfirmed"));
                activity.Complete(HostOperationOutcome.Failed);
                throw;
            }
        }, static _ => HostTaskState.Failed, token);
}
