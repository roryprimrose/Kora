using Kora.Core.Commands;
using Kora.Core.Memory;
using Kora.Core.Storage;

namespace Kora.Application.Memory;

internal sealed partial class MemoryManagementService
{
    internal async Task<SessionMemoryCopyReview> InspectSessionCopyAsync(SessionWorkspaceEntry target,
        MemoryRecord source, Func<bool> admitted, CancellationToken token)
    {
        var input = RequireModelProposalInput();
        await RunMemorySourceReviewAsync(target, admitted, async (_, live) =>
        {
            lock (stateGate)
            {
                if (!inspections.TryGetValue(source.Id, out var inspected)
                    || inspected.Record != source || inspected.Control != access.ControlRevision)
                {
                    throw new InvalidOperationException("Select and inspect ONE exact current reviewed Session memory first.");
                }
            }
            await ValidateSessionCopySourceAsync(target, source, token).ConfigureAwait(false);
            return new("observed", "Complete source and independent scope/lifetime review; no candidate created.");
        }, token).ConfigureAwait(false);
        return new(this, target, source, input, access.ControlRevision, contextTime.GetUtcNow().AddSeconds(60), admitted);
    }

    internal Task<MemoryResult> ProposeSessionCopyAsync(SessionMemoryCopyReview review, MemoryContentClass classification,
        CancellationToken token) => ActOnSessionCopyAsync(review, MemoryOperation.Propose, classification, true, token);

    internal Task<MemoryResult> ReviewSessionCopyAsync(SessionMemoryCopyReview review, bool accept,
        CancellationToken token) => ActOnSessionCopyAsync(review, MemoryOperation.Review, default, accept, token);

    internal Task<MemoryResult> AdmitSessionCopyAsync(SessionMemoryCopyReview review, CancellationToken token) =>
        ActOnSessionCopyAsync(review, MemoryOperation.Admit, default, false, token);

    private async Task<MemoryResult> ActOnSessionCopyAsync(SessionMemoryCopyReview review, MemoryOperation operation,
        MemoryContentClass classification, bool accept, CancellationToken token)
    {
        var input = RequireModelProposalInput();
        if (!ReferenceEquals(review.Owner, this))
        { throw new InvalidOperationException("The scope-change review belongs to another memory owner."); }
        var record = review.Take(input, operation);
        MemoryResult? result = null;
        try
        {
            await RunMemorySourceReviewAsync(review.Target, () => review.Current, async (_, live) =>
            {
                await ValidateSessionCopySourceAsync(review.Target, review.Source, token).ConfigureAwait(false);
                var rows = await profileAdmission.ObserveSessionAsync(token).ConfigureAwait(false);
                if (record is not null && !rows.Contains(record))
                { throw new InvalidOperationException("The exact profile candidate changed or was retired."); }
                result = operation switch
                {
                    MemoryOperation.Propose => await profileAdmission.ProposeSessionCopyAsync(
                        new(classification, review.Source.Candidate!.Value),
                        await profileAdmission.ResolveDeviceProfileScopeAsync(token).ConfigureAwait(false), review.Source, token).ConfigureAwait(false),
                    MemoryOperation.Review => await profileAdmission.ReviewSessionCopyAsync(record!, review.Source, accept, token).ConfigureAwait(false),
                    _ => await profileAdmission.AdmitSessionCopyAsync(record!, review.Source, token).ConfigureAwait(false),
                };
                if (result.Record is { } changed) { review.Transitioned(changed); }
                return new(result.Outcome.ToString(), result.Reason.ToString());
            }, token).ConfigureAwait(false);
            if (result!.Outcome != MemoryOutcome.Succeeded || operation == MemoryOperation.Admit
                || operation == MemoryOperation.Review && !accept)
            { review.Dispose(); }
            return result;
        }
        catch
        {
            review.Dispose();
            throw;
        }
    }

    private async Task ValidateSessionCopySourceAsync(SessionWorkspaceEntry target, MemoryRecord source, CancellationToken token)
    {
        var current = await workspace.ReadMetadataAsync(target.Authority.SessionId, token).ConfigureAwait(false);
        var rows = await Admission.ObserveSessionAsync(token).ConfigureAwait(false);
        if (current.Authority != target.Authority
            || source.Review != MemoryReviewState.Admitted || source.Retention != MemoryRetentionState.Enabled
            || source.Candidate is null || !rows.Contains(source))
        {
            throw new InvalidOperationException("The exact reviewed Session source is no longer current. This copy is retired; inspect current memory before a new request.");
        }
    }

    internal void RetireSessionCopy(MemoryRecord record) => profileAdmission.RetireSessionCopy(record);
}
