using Kora.Application.Hosting;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora.Application.Interaction;

public sealed class ExactGrantControlAdmission(ISessionWorkspaceStore workspace, IExactGrantStore store,
    HostTaskCoordinator tasks, HostAuthorizationService authorization) : HostControlAdmission(workspace, tasks)
{
    protected override ValueTask<WorkSessionAuthorization> CreateSessionAsync(
        HostRequest request, Func<bool> eligible, CancellationToken token) =>
        store.CreateExactGrantControlSessionAsync(request, eligible, token);

    public Task<ExactGrantBatchDecision> RevokeBatchAsync(ExactGrantBatch preview, Func<bool> eligible, CancellationToken token)
    {
        preview.Validate();
        if (HostActivity.Current is not null)
        {
            throw new InvalidOperationException("Start a fresh native exact-batch confirmation, not an ambient request.");
        }
        return RunCommittedAsync(RequestOrigin.LocalUi, eligible, async (request, session, currentEligible, admittedToken) =>
        {
            try
            {
                var result = await authorization.RevokeExactBatchAsync(request, session.Generation, preview,
                    currentEligible, admittedToken).ConfigureAwait(false);
                try
                {
                    if (!currentEligible()) { throw new IOException("No late batch receipt is admitted."); }
                    if (result.Outcome == HostInteractionOutcome.Revoked)
                    {
                        if (result.RevokedGrants.Length != preview.Records.Length)
                        {
                            throw new IOException("The batch receipt is incomplete.");
                        }
                        for (var index = 0; index < preview.Records.Length; index++)
                        {
                            var expected = preview.Records[index].Grant.Revoke("explicit-user-revocation");
                            var current = await store.InspectExactGrantAsync(expected.Id, admittedToken).ConfigureAwait(false);
                            if (result.RevokedGrants[index] != expected || current?.Grant != expected || !currentEligible())
                            {
                                throw new IOException("Exact batch readback changed or is unavailable.");
                            }
                        }
                    }
                    else if (result.Outcome != HostInteractionOutcome.Conflict || !result.RevokedGrants.IsEmpty)
                    {
                        throw new IOException("The batch receipt does not certify all-or-nothing revocation.");
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    throw new IOException("Batch transaction completed but receipt certainty was lost. Inspect current authority; no rollback or partial success is claimed.", exception);
                }
                return result;
            }
            catch (IOException) { HoldEvidenceUnavailable(); throw; }
            catch (InvalidDataException) { HoldEvidenceUnavailable(); throw; }
        }, result => result.Outcome == HostInteractionOutcome.Revoked ? HostTaskState.Succeeded : HostTaskState.Failed, token);
    }

    public Task<HostInteractionDecision> RevokeAsync(ExactGrantInspection preview,
        Func<bool> eligible, CancellationToken token)
    {
        // This surface accepts a new native click only. Stored/ambient request lineage cannot be adopted.
        if (HostActivity.Current is not null)
        {
            throw new InvalidOperationException("Start a fresh native exact-grant confirmation, not an ambient request.");
        }
        return RunCommittedAsync(RequestOrigin.LocalUi, eligible, async (request, session, currentEligible, admittedToken) =>
        {
            try
            {
                var result = await authorization.RevokeExactAsync(request, session.Generation, preview,
                    currentEligible, admittedToken).ConfigureAwait(false);
                try
                {
                    if (!currentEligible()) { throw new IOException("Revocation committed outcome requires explicit current inspection; no late receipt is presented."); }
                    var current = await store.InspectExactGrantAsync(preview.Grant.Id, admittedToken).ConfigureAwait(false);
                    if (result.Grant is { } revoked && (current?.Grant != revoked || !currentEligible()))
                    {
                        throw new IOException("Revocation readback is unavailable or changed; inspect current authority before retrying.");
                    }
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    throw new IOException("Revocation transaction completed but readback or receipt certainty was lost. Inspect authority; no rollback or late success is claimed.", exception);
                }
                return result;
            }
            catch (IOException) { HoldEvidenceUnavailable(); throw; }
            catch (InvalidDataException) { HoldEvidenceUnavailable(); throw; }
        }, result => result.Outcome == HostInteractionOutcome.Revoked ? HostTaskState.Succeeded : HostTaskState.Failed, token);
    }
}