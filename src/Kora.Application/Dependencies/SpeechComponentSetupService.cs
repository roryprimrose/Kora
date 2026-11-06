using Kora.Application.Auditing;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Dependencies;

public sealed partial class SpeechComponentSetupService(
    SpeechComponentCatalogue catalogue, ISpeechComponentStore store, ISpeechComponentRuntime runtime,
    ISecurityAuditLog audit, ILogger<SpeechComponentSetupService> logger, TimeProvider clock)
{
    private readonly Guid owner = Guid.NewGuid();

    public IReadOnlyList<SpeechComponentDefinition> Components => catalogue.Components;
    public IReadOnlyList<SpeechComponentDefinition> Offers =>
        catalogue.Components.Where(component => component.Qualified
            && ObserveRuntime(component).Compatibility == SpeechComponentCompatibility.Compatible).ToArray();

    public async Task<SpeechComponentReadiness> InspectAsync(SpeechComponentIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var component = catalogue.Resolve(identity);
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Runtime);
        SpeechComponentSnapshot? storage = null;
        try
        {
            storage = await store.InspectAsync(component, cancellationToken).ConfigureAwait(false);
            var result = new SpeechComponentReadiness(identity, storage, ObserveRuntime(component), component.Qualified);
            activity.Complete(HostOperationOutcome.Completed);
            return result;
        }
        catch (OperationCanceledException)
        {
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            InspectionFailed(logger);
            activity.Complete(HostOperationOutcome.Failed);
            return new(identity, storage,
                new(SpeechComponentCompatibility.Unknown, SpeechComponentFunctionalEvidence.Unknown,
                    "Current runtime evidence could not be established by this inspection."),
                component.Qualified, "Inspection failed; local data state must not be inferred as missing or ready. "
                    + "Resolve exact ownership/access and inspect again. " + exception.Message);
        }
    }

    public async Task<SpeechComponentSetupPlan> ReviewAsync(SpeechComponentIdentity identity,
        SpeechComponentOperation operation, Func<bool> admitted, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(admitted);
        if (!Enum.IsDefined(operation)) { throw new ArgumentOutOfRangeException(nameof(operation)); }
        var request = HostActivity.RequireCurrent().Request;
        if (request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || !admitted())
        {
            throw new InvalidOperationException("Optional speech setup requires a current original-user host request.");
        }
        var component = catalogue.Resolve(identity);
        var readiness = await InspectAsync(identity, cancellationToken).ConfigureAwait(false);
        if (!readiness.CanOffer || !admitted())
        {
            throw new InvalidOperationException("This exact speech component is not offered for setup. " + readiness.Description);
        }
        if (operation == SpeechComponentOperation.Remove
            && (request.Origin != RequestOrigin.LocalUi
                || readiness.Runtime is not { NativeIdentity: not null, ConsumersKnown: true, Dependents: not null }
                || readiness.Storage is not
                {
                    Ownership: SpeechComponentOwnership.Managed or SpeechComponentOwnership.RemovalPending,
                    ExactBytes: not null, Inventory: not null
                }))
        {
            throw new InvalidOperationException("Removal requires a native original-user review of a known exact managed inventory. Unknown, shared and absent data cannot be confirmed.");
        }
        return new(owner, component, operation, readiness, request, admitted, clock.GetUtcNow().AddMinutes(5));
    }

    public async Task<SpeechComponentReadiness> ApplyAsync(SpeechComponentSetupPlan plan, bool consent,
        IProgress<string> progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(progress);
        if (plan.Owner != owner || !plan.Consume())
        {
            throw new InvalidOperationException("The exact speech setup review is foreign or already consumed. Inspect and review again.");
        }
        var request = HostActivity.RequireCurrent().Request;
        if (!ReferenceEquals(request, plan.Request))
        {
            throw new InvalidOperationException("The speech setup review belongs to a different host request.");
        }
        var auditEvent = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ResourceWrite,
            $"speech-component.{plan.Operation.ToString().ToLowerInvariant()}", SecurityAuditOutcome.Requested,
            request.Origin == RequestOrigin.ActivatedVoice ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser,
            plan.Component.Identity.ComponentId);
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Storage);
        await audit.WriteAsync(auditEvent, cancellationToken).ConfigureAwait(false);
        var committed = false;
        var denied = false;
        var failureReason = "inspect-before-retry";
        Exception failure;
        SecurityAuditEvent failureAudit;
        HostOperationOutcome failureOutcome;
        try
        {
            bool Admitted() => consent && clock.GetUtcNow() < plan.Expires && plan.Admitted()
                && ReferenceEquals(catalogue.Resolve(plan.Component.Identity), plan.Component)
                && plan.Component.Qualified
                && ObserveRuntime(plan.Component) is { Compatibility: SpeechComponentCompatibility.Compatible } observedRuntime
                && observedRuntime.ConsumerRevision == plan.Readiness.Runtime.ConsumerRevision
                && string.Equals(observedRuntime.NativeIdentity, plan.Readiness.Runtime.NativeIdentity, StringComparison.Ordinal)
                && (plan.Operation != SpeechComponentOperation.Remove || observedRuntime.ConsumersKnown);
            if (!Admitted())
            {
                denied = true;
                failureReason = "review-not-admitted";
                throw new UnauthorizedAccessException("Speech setup was not approved or the reviewed host state changed.");
            }
            var mutation = await runtime.TryAcquireExclusiveAsync(plan.Component, cancellationToken).ConfigureAwait(false);
            if (mutation is null || mutation.State != SpeechComponentMutationState.Acquired)
            {
                denied = true;
                var refusal = mutation ?? new SpeechComponentMutationAdmission(SpeechComponentMutationState.UnknownOwnership);
                failureReason = refusal.ReasonCode;
                throw new SpeechComponentMutationDeniedException(refusal);
            }
            SpeechComponentReadiness result;
            await using (var lease = mutation.RequireLease())
            {
                var current = await store.InspectAsync(plan.Component, cancellationToken).ConfigureAwait(false);
                if (current != plan.ExpectedStorage || !Admitted())
                {
                    throw new InvalidOperationException("Speech component state changed. Inspect and explicitly review the exact current state before retrying.");
                }
                progress.Report("Exact identity and exclusive runtime ownership checked.");
                if (plan.Operation == SpeechComponentOperation.Prepare)
                {
                    await store.PrepareAsync(plan.Component, current, Admitted, progress, cancellationToken).ConfigureAwait(false);
                }
                else if (plan.Operation == SpeechComponentOperation.Remove)
                {
                    runtime.HoldUnavailable(plan.Component);
                    await store.RemoveAsync(plan.Component, current, Admitted, cancellationToken).ConfigureAwait(false);
                }
                committed = plan.Operation != SpeechComponentOperation.Verify;
                result = await InspectAsync(plan.Component.Identity, cancellationToken).ConfigureAwait(false);
                if (result.Storage is not { } observed)
                {
                    throw new InvalidDataException(result.Description);
                }
                if (plan.Operation == SpeechComponentOperation.Remove)
                {
                    if (observed.Installed || observed.Ownership != SpeechComponentOwnership.Missing)
                    {
                        throw new InvalidDataException("Removal did not reconcile to the exact absent component.");
                    }
                }
                else
                {
                    if (!observed.Verified || !Admitted())
                    {
                        throw new InvalidDataException("Component integrity or host admission failed. Data may remain installed but is not usable. Inspect before retrying.");
                    }
                    var functional = await runtime.VerifyAsync(plan.Component, cancellationToken).ConfigureAwait(false)
                        ?? throw new InvalidDataException("The host returned no authoritative functional evidence. Current state is Unknown.");
                    if (functional.Compatibility != SpeechComponentCompatibility.Compatible
                        || functional.Functional != SpeechComponentFunctionalEvidence.Verified)
                    {
                        throw new InvalidDataException($"Functional verification is {functional.Functional}. {functional.Detail} "
                            + "Data may remain installed but no runtime was enabled. Inspect before retrying.");
                    }
                    var rechecked = await InspectAsync(plan.Component.Identity, cancellationToken).ConfigureAwait(false);
                    if (rechecked.Storage != observed || !rechecked.CanOffer || !Admitted())
                    {
                        throw new InvalidDataException("Speech component readiness changed during verification. No runtime was enabled.");
                    }
                    result = rechecked with { Runtime = functional };
                }
                if (!Admitted())
                {
                    throw new UnauthorizedAccessException("Host admission changed after asset reconciliation. Inspect the exact outcome; no runtime was selected.");
                }

            }
            await audit.WriteAsync(auditEvent.WithOutcome(SecurityAuditOutcome.Succeeded), CancellationToken.None).ConfigureAwait(false);
            Completed(logger, plan.Operation);
            activity.Complete(HostOperationOutcome.Completed);
            return result;
        }
        catch (OperationCanceledException exception)
        {
            Cancelled(logger, plan.Operation);
            failure = exception;
            failureAudit = auditEvent.WithOutcome(SecurityAuditOutcome.Cancelled,
                committed ? "reconcile-after-cancel" : "cancelled");
            failureOutcome = HostOperationOutcome.Cancelled;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or HttpRequestException or TimeoutException)
        {
            if (exception is SpeechComponentMutationDeniedException refusal)
            {
                MutationDenied(logger, plan.Operation, refusal.State);
            }
            else { Failed(logger, plan.Operation); }
            failure = exception;
            failureAudit = auditEvent.WithOutcome(denied ? SecurityAuditOutcome.Denied : SecurityAuditOutcome.Failed, failureReason);
            failureOutcome = HostOperationOutcome.Failed;
        }
        if (plan.Operation == SpeechComponentOperation.Remove && plan.Admitted())
        {
            var recovery = await InspectAsync(plan.Component.Identity, CancellationToken.None).ConfigureAwait(false);
            if (plan.Admitted()) { plan.RemovalRecovery = recovery; }
        }
        await audit.WriteAsync(failureAudit, CancellationToken.None).ConfigureAwait(false);
        activity.Complete(failureOutcome);
        return await Task.FromException<SpeechComponentReadiness>(failure).ConfigureAwait(false);
    }

    private SpeechComponentRuntimeObservation ObserveRuntime(SpeechComponentDefinition component)
    {
        try
        {
            return runtime.Observe(component)
                ?? throw new InvalidOperationException("The installed adapter returned no readiness observation.");
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException or InvalidOperationException)
        {
            InspectionFailed(logger);
            return new(SpeechComponentCompatibility.Unknown, SpeechComponentFunctionalEvidence.Unknown,
                "Installed runtime evidence could not be read. Resolve adapter access and inspect again; no probe was run.");
        }
    }
}