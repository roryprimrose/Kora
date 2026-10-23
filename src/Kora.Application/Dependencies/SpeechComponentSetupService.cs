using Kora.Application.Auditing;
using Kora.Core.Auditing;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Dependencies;

public sealed partial class SpeechComponentSetupService(
    SpeechComponentCatalogue catalogue, ISpeechComponentStore store, ISpeechComponentRuntime runtime,
    ISecurityAuditLog audit, ILogger<SpeechComponentSetupService> logger, TimeProvider clock) : ISpeechComponentReadiness
{
    private readonly Guid owner = Guid.NewGuid();

    public IReadOnlyList<SpeechComponentDefinition> Offers =>
        catalogue.Components.Where(component => component.Qualified && runtime.IsCompatible(component)).ToArray();

    public async Task<SpeechComponentReadiness> InspectAsync(SpeechComponentIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        var component = catalogue.Resolve(identity);
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Runtime);
        try
        {
            var storage = await store.InspectAsync(component, cancellationToken).ConfigureAwait(false);
            var result = new SpeechComponentReadiness(identity, storage, runtime.IsCompatible(component), component.Qualified);
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
            InspectionFailed(logger, exception);
            activity.Complete(HostOperationOutcome.Failed);
            throw;
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
            throw new InvalidOperationException("This exact speech component is not qualified and compatible for setup.");
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
        try
        {
            bool Admitted() => consent && clock.GetUtcNow() < plan.Expires && plan.Admitted()
                && ReferenceEquals(catalogue.Resolve(plan.Component.Identity), plan.Component)
                && plan.Component.Qualified && runtime.IsCompatible(plan.Component);
            if (!Admitted())
            {
                denied = true;
                throw new UnauthorizedAccessException("Speech setup was not approved or the reviewed host state changed.");
            }
            SpeechComponentReadiness result;
            await using (var lease = await runtime.TryAcquireExclusiveAsync(plan.Component, cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Speech assets are in use, shared, or ownership/quiescence is unknown. No assets changed."))
            {
                var current = await store.InspectAsync(plan.Component, cancellationToken).ConfigureAwait(false);
                if (current != plan.Readiness.Storage || !Admitted())
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
                    await store.RemoveAsync(plan.Component, current, Admitted, cancellationToken).ConfigureAwait(false);
                }
                committed = plan.Operation != SpeechComponentOperation.Verify;
                result = await InspectAsync(plan.Component.Identity, cancellationToken).ConfigureAwait(false);
                if (plan.Operation == SpeechComponentOperation.Remove)
                {
                    if (result.Storage.Installed || result.Storage.Ownership != SpeechComponentOwnership.Missing)
                    {
                        throw new InvalidDataException("Removal did not reconcile to the exact absent component.");
                    }
                }
                else
                {
                    if (!result.Storage.Verified || !Admitted()
                        || !await runtime.VerifyAsync(plan.Component, cancellationToken).ConfigureAwait(false))
                    {
                        throw new InvalidDataException("Component bytes or the inert runtime check failed. Data may remain installed but is not usable. Inspect before retrying.");
                    }
                    var rechecked = await InspectAsync(plan.Component.Identity, cancellationToken).ConfigureAwait(false);
                    if (rechecked != result || !Admitted())
                    {
                        throw new InvalidDataException("Speech component readiness changed during verification. No runtime was enabled.");
                    }
                    result = result with { FunctionalVerified = true };
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
        catch (OperationCanceledException)
        {
            Cancelled(logger, plan.Operation);
            await audit.WriteAsync(auditEvent.WithOutcome(SecurityAuditOutcome.Cancelled,
                committed ? "reconcile-after-cancel" : "cancelled"), CancellationToken.None).ConfigureAwait(false);
            activity.Complete(HostOperationOutcome.Cancelled);
            throw;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException
            or InvalidOperationException or HttpRequestException or TimeoutException)
        {
            Failed(logger, plan.Operation, exception);
            await audit.WriteAsync(auditEvent.WithOutcome(denied ? SecurityAuditOutcome.Denied : SecurityAuditOutcome.Failed,
                denied ? "review-not-admitted" : "inspect-before-retry"),
                CancellationToken.None).ConfigureAwait(false);
            activity.Complete(HostOperationOutcome.Failed);
            throw;
        }
    }
}
