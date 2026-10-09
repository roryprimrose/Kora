using Kora.Core.Auditing;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Memory;
using Kora.Core.Storage;
using Kora.Core.Tools;
using Microsoft.Extensions.Logging;

namespace Kora.Application.Memory;

/// <summary>Bounded host-owned admission workspace. It is not durable storage or a model-facing tool.</summary>
internal sealed partial class MemoryAdmissionService(
    ISessionWorkspaceStore workspace, ISessionWorkspaceAccess access, ICapabilityHostAccess host,
    IMemoryScopeAccess scopes, ISecurityAuditLog audit, ILogger<MemoryAdmissionService> logger,
    TimeProvider time) : IDisposable
{
    private readonly Dictionary<HostId<MemoryIdentity>, MemoryRecord> records = [];
    private readonly Lock stateGate = new();
    private long lifecycleRevision;
    private bool disposed;

    internal Task<MemoryResult> ProposeAsync(MemoryCandidate? candidate, MemoryScope? scope,
        MemoryProposalOrigin origin, CancellationToken token) =>
        ExecuteAsync(MemoryOperation.Propose, null, default, scope, candidate, origin, false, MemoryDestination.Unknown, token);

    internal Task<MemoryResult> ReviewAsync(HostId<MemoryIdentity> id, HostRevision revision, bool accept,
        CancellationToken token) =>
        ExecuteAsync(MemoryOperation.Review, id, revision, null, null, MemoryProposalOrigin.Unknown, accept, MemoryDestination.Unknown, token);

    internal Task<MemoryResult> AdmitAsync(HostId<MemoryIdentity> id, HostRevision revision, CancellationToken token) =>
        ExecuteAsync(MemoryOperation.Admit, id, revision, null, null, MemoryProposalOrigin.Unknown, false, MemoryDestination.Unknown, token);

    internal Task<MemoryResult> EditAsync(HostId<MemoryIdentity> id, HostRevision revision,
        MemoryCandidate? replacement, CancellationToken token) =>
        ExecuteAsync(MemoryOperation.Edit, id, revision, null, replacement, MemoryProposalOrigin.User, false, MemoryDestination.Unknown, token);

    internal Task<MemoryResult> DisableAsync(HostId<MemoryIdentity> id, HostRevision revision, CancellationToken token) =>
        ExecuteAsync(MemoryOperation.Disable, id, revision, null, null, MemoryProposalOrigin.Unknown, false, MemoryDestination.Unknown, token);

    internal Task<MemoryResult> ForgetAsync(HostId<MemoryIdentity> id, HostRevision revision, CancellationToken token) =>
        ExecuteAsync(MemoryOperation.Forget, id, revision, null, null, MemoryProposalOrigin.Unknown, false, MemoryDestination.Unknown, token);

    internal Task<MemoryResult> UseAsync(HostId<MemoryIdentity> id, HostRevision revision,
        MemoryDestination destination, CancellationToken token) =>
        ExecuteAsync(MemoryOperation.Use, id, revision, null, null, MemoryProposalOrigin.Unknown, false, destination, token);

    private async Task<MemoryResult> ExecuteAsync(MemoryOperation operation, HostId<MemoryIdentity>? id,
        HostRevision expected, MemoryScope? scope, MemoryCandidate? candidate, MemoryProposalOrigin origin,
        bool accept, MemoryDestination destination, CancellationToken token)
    {
        var issuer = HostActivity.Current;
        if (issuer is null || issuer.Activity!.IsStopped || issuer.Outcome != HostOperationOutcome.Unknown)
        {
            return Finish(new(MemoryOutcome.Denied, MemoryReason.HostContextRequired), operation);
        }
        var request = issuer.Request;
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Policy);
        var continuation = HostActivity.CaptureContinuation(HostActivityLayer.Application, HostOperation.Policy);
        var requested = new SecurityAuditEvent(Guid.NewGuid(), SecurityAuditCategory.ProtectedOperation,
            "memory." + operation.ToString().ToLowerInvariant(), SecurityAuditOutcome.Requested,
            SecurityAuditInitiator.System, id?.Value.ToString("D") ?? request.RequestId.Value.ToString("D"));
        try
        {
            audit.Write(requested);
            token.ThrowIfCancellationRequested();
            MemoryRecord? current;
            long lifecycle;
            lock (stateGate)
            {
                lifecycle = lifecycleRevision;
                current = id is { } identity ? records.GetValueOrDefault(identity) : null;
            }
            if (operation != MemoryOperation.Propose)
            {
                scope ??= current?.Scope ?? MemoryScope.Session(request.SessionId);
            }
            var control = access.ControlRevision;
            var before = scopes.Observe(request);
            MemoryResult result;
            var reason = MemoryPolicy.CheckBoundary(scope, before);
            if (reason != MemoryReason.None)
            {
                result = new(MemoryOutcome.Denied, reason);
            }
            else if (!HostEligible(control, issuer))
            {
                result = new(MemoryOutcome.Denied, MemoryReason.AuthorityClosed);
            }
            else if (operation != MemoryOperation.Use && !(operation == MemoryOperation.Propose && origin == MemoryProposalOrigin.Model)
                && request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice))
            {
                result = new(MemoryOutcome.Denied, MemoryReason.UserIntentRequired);
            }
            else
            {
                var session = await workspace.ReadMetadataAsync(request.SessionId, token).ConfigureAwait(false);
                lock (stateGate)
                {
                    // No await follows this fence: lifecycle callbacks cannot interleave with mutation.
                    if (token.IsCancellationRequested)
                    {
                        result = new(MemoryOutcome.Cancelled, MemoryReason.CallerCancelled);
                    }
                    else if (lifecycle != lifecycleRevision || !HostEligible(control, issuer)
                        || scopes.Observe(request) != before || before!.Session != request.SessionId
                        || session.Authority.SessionId != request.SessionId || !session.Authority.IsActive
                        || session.Authority.Generation != before.Generation)
                    {
                        result = new(MemoryOutcome.Denied, MemoryReason.AuthorityClosed);
                    }
                    else
                    {
                        current = id is { } identity ? records.GetValueOrDefault(identity) : null;
                        result = Transition(operation, current, expected, scope!, candidate, origin,
                            accept, destination, before, request);
                        // Audit must succeed before publishing the new state or use receipt.
                        CompleteAudit(requested, result, continuation);
                        var unchanged = id is not { } known || ReferenceEquals(records.GetValueOrDefault(known), current);
                        if (token.IsCancellationRequested || lifecycle != lifecycleRevision || !HostEligible(control, issuer)
                            || scopes.Observe(request) != before || !unchanged)
                        {
                            result = token.IsCancellationRequested
                                ? new(MemoryOutcome.Cancelled, MemoryReason.CallerCancelled)
                                : !unchanged ? new(MemoryOutcome.RevisionConflict, MemoryReason.RevisionMismatch)
                                : new(MemoryOutcome.Denied, MemoryReason.AuthorityClosed);
                            CompleteAudit(requested, result, continuation);
                        }
                        if (result.Record is { } updated && result.Outcome == MemoryOutcome.Succeeded
                            && operation != MemoryOperation.Use)
                        {
                            records[updated.Id] = updated;
                        }
                        activity.Complete(ToActivityOutcome(result.Outcome));
                        return Finish(result, operation, continuation);
                    }
                }
            }
            CompleteAudit(requested, result, continuation);
            activity.Complete(ToActivityOutcome(result.Outcome));
            return Finish(result, operation, continuation);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            var result = new MemoryResult(MemoryOutcome.Cancelled, MemoryReason.CallerCancelled);
            CompleteAudit(requested, result, continuation);
            activity.Complete(HostOperationOutcome.Cancelled);
            return Finish(result, operation, continuation);
        }
        catch (Exception exception)
        {
            using var recovery = HostActivity.Current is null ? continuation() : null;
            Failure(logger, exception.GetType().Name);
            activity.Complete(HostOperationOutcome.Unknown);
            WriteAudit(requested.WithOutcome(SecurityAuditOutcome.Unknown), continuation);
            throw;
        }
    }

    private MemoryResult Transition(MemoryOperation operation, MemoryRecord? current, HostRevision expected,
        MemoryScope scope, MemoryCandidate? candidate, MemoryProposalOrigin origin, bool accept,
        MemoryDestination destination, MemoryBoundary boundary, HostRequest request)
    {
        if (operation == MemoryOperation.Propose)
        {
            if (origin is not (MemoryProposalOrigin.User or MemoryProposalOrigin.Model))
            {
                return new(MemoryOutcome.Denied, MemoryReason.LineageUnknown);
            }
            var validation = MemoryPolicy.ValidateCandidate(candidate);
            if (validation != MemoryReason.None) { return new(MemoryOutcome.Denied, validation); }
            if (records.Count >= MemoryPolicy.MaximumEntries) { return new(MemoryOutcome.CapacityExceeded, MemoryReason.Capacity); }
            var lineage = new MemoryLineage(request, boundary.Generation, boundary.Profile, origin, boundary.Source, boundary.SourceRevision);
            return new(MemoryOutcome.Succeeded, MemoryReason.None,
                new(new(Guid.NewGuid()), new(1), scope, lineage, candidate, time.GetUtcNow(),
                    MemoryReviewState.Proposed, MemoryRetentionState.Pending, null));
        }
        if (current is null) { return new(MemoryOutcome.NotFound, MemoryReason.Missing); }
        if (current.Revision != expected) { return new(MemoryOutcome.RevisionConflict, MemoryReason.RevisionMismatch); }
        var lineageReason = MemoryPolicy.CheckLineage(current, boundary);
        if (lineageReason != MemoryReason.None)
        {
            return new(MemoryOutcome.Denied, lineageReason);
        }
        if (operation == MemoryOperation.Use)
        {
            var eligibility = MemoryPolicy.Eligible(current, boundary, destination);
            return eligibility == MemoryReason.None
                ? new(MemoryOutcome.Succeeded, MemoryReason.None, Use:
                    new(current.Id, current.Revision, current.Scope, current.Lineage, current.Candidate!, current.Receipt!, request))
                : new(MemoryOutcome.Denied, eligibility);
        }
        if (current.Retention == MemoryRetentionState.Forgotten)
        {
            return new(MemoryOutcome.InvalidTransition, MemoryReason.StateMismatch);
        }
        var revision = new HostRevision(checked(current.Revision.Value + 1));
        MemoryRecord updated;
        switch (operation)
        {
            case MemoryOperation.Review when current.Review == MemoryReviewState.Proposed:
                updated = current with
                {
                    Revision = revision,
                    Review = accept ? MemoryReviewState.Reviewed : MemoryReviewState.Rejected,
                    Receipt = accept ? new(request.RequestId, revision, time.GetUtcNow(), boundary) : null,
                };
                break;
            case MemoryOperation.Admit when MemoryPolicy.CanAdmit(current, boundary):
                updated = current with { Revision = revision, Review = MemoryReviewState.Admitted, Retention = MemoryRetentionState.Enabled };
                break;
            case MemoryOperation.Edit:
                var validation = MemoryPolicy.ValidateCandidate(candidate);
                if (validation != MemoryReason.None) { return new(MemoryOutcome.Denied, validation); }
                updated = current with
                {
                    Revision = revision, Candidate = candidate, Review = MemoryReviewState.Proposed,
                    Retention = MemoryRetentionState.Pending, Receipt = null,
                    Lineage = new(request, boundary.Generation, boundary.Profile, MemoryProposalOrigin.User, boundary.Source, boundary.SourceRevision),
                };
                break;
            case MemoryOperation.Disable when current.Retention == MemoryRetentionState.Enabled:
                updated = current with { Revision = revision, Retention = MemoryRetentionState.Disabled };
                break;
            case MemoryOperation.Forget:
                updated = current with { Revision = revision, Candidate = null, Receipt = null, Retention = MemoryRetentionState.Forgotten };
                break;
            default:
                return new(MemoryOutcome.InvalidTransition, MemoryReason.StateMismatch);
        }
        return new(MemoryOutcome.Succeeded, MemoryReason.None, updated);
    }

    private bool HostEligible(long control, HostActivity issuer) =>
        !disposed && host.IsCurrentHost && access.CanControl && access.ControlRevision == control
        && !issuer.Activity!.IsStopped && issuer.Outcome == HostOperationOutcome.Unknown;

    internal void ExpireSession(HostId<SessionIdentity> session)
    {
        session.Validate();
        lock (stateGate)
        {
            lifecycleRevision++;
            foreach (var item in records.Values.Where(item => item.Scope.Kind == MemoryScopeKind.Session
                && item.Scope.Identity == session.Value).ToArray())
            {
                records[item.Id] = item with
                {
                    Revision = new(checked(item.Revision.Value + 1)), Candidate = null, Receipt = null,
                    Retention = MemoryRetentionState.Forgotten,
                };
            }
        }
    }

    public void Dispose()
    {
        lock (stateGate)
        {
            disposed = true;
            lifecycleRevision++;
            records.Clear();
        }
        GC.SuppressFinalize(this);
    }

    private void CompleteAudit(SecurityAuditEvent requested, MemoryResult result, Func<HostActivity> continuation) =>
        WriteAudit(requested.WithOutcome(result.Outcome switch
        {
            MemoryOutcome.Succeeded => SecurityAuditOutcome.Succeeded,
            MemoryOutcome.Cancelled => SecurityAuditOutcome.Cancelled,
            _ => SecurityAuditOutcome.Denied,
        }), continuation);

    private void WriteAudit(SecurityAuditEvent auditEvent, Func<HostActivity> continuation)
    {
        using var recovery = HostActivity.Current is null ? continuation() : null;
        audit.Write(auditEvent);
        recovery?.Complete(HostOperationOutcome.Failed);
    }

    private static HostOperationOutcome ToActivityOutcome(MemoryOutcome outcome) => outcome switch
    {
        MemoryOutcome.Succeeded => HostOperationOutcome.Completed,
        MemoryOutcome.Cancelled => HostOperationOutcome.Cancelled,
        _ => HostOperationOutcome.Failed,
    };

    private MemoryResult Finish(MemoryResult result, MemoryOperation operation, Func<HostActivity>? continuation = null)
    {
        using var recovery = HostActivity.Current is null ? continuation?.Invoke() : null;
        Result(logger, operation, result.Outcome, result.Reason, result.Record?.Id.Value ?? result.Use?.Id.Value);
        recovery?.Complete(ToActivityOutcome(result.Outcome));
        return result;
    }
}
