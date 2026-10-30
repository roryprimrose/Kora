using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Hosting;

namespace Kora.Application.Definitions;

public sealed partial class OwnedInstructionEditorActions
{
    private IOwnedInstructionRenameStore RenameStore => Kind == ArtifactKind.Instruction && store is IOwnedInstructionRenameStore renames
        ? renames : throw new InvalidOperationException("Native owned Instruction rename/recovery is unavailable for this source.");

    public Task<OwnedInstructionRenameReview> ReviewRename(OwnedInstructionSnapshot source, string destination,
        Func<bool> eligible, CancellationToken token) =>
        admission.Run(eligible, async (_, _, current, admitted) =>
        {
            _ = RenameStore;
            if (!IsRefinementSourceObserved(source))
            { throw new InvalidOperationException("Reload the current exact owned source catalogue before a new rename review; excluded or unavailable sources cannot be substituted."); }
            var proposal = new OwnedInstructionRename(source, destination);
            ValidateRenameNamespace(proposal);
            var live = await store.Read(source.Name, admitted).ConfigureAwait(false);
            var occupied = await store.Read(destination, admitted).ConfigureAwait(false);
            if (!current() || live is null || !string.Equals(live.Revision, source.Revision, StringComparison.Ordinal) || occupied is not null)
            { throw new InvalidOperationException("The rename source changed or the new ID is occupied. Inspect and review again; no overwrite is available."); }
            return new OwnedInstructionRenameReview(proposal, catalogue.Candidates, null);
        }, token);

    public Task<OwnedInstructionRenameReview?> ReviewRenameRecovery(Func<bool> eligible, CancellationToken token) =>
        admission.Run(eligible, async (_, _, current, admitted) =>
        {
            var recovery = await RenameStore.ReadRenameRecovery(admitted).ConfigureAwait(false);
            if (!current()) { throw new InvalidOperationException("Rename recovery inspection admission changed."); }
            return recovery is null ? null : new OwnedInstructionRenameReview(recovery.Proposal, catalogue.Candidates, recovery);
        }, token);

    private void ValidateRenameNamespace(OwnedInstructionRename proposal)
    {
        var candidates = catalogue.Candidates.Where(item => !Matches(item, proposal.Source.Name)).ToArray();
        ValidateProposal(proposal.Proposed, proposal.DestinationName, candidates);
        if (candidates.Any(item => Matches(item, proposal.DestinationName)))
        { throw new InvalidOperationException("The destination is already an observed owned ID. Rename never overwrites it."); }
    }

    public Task<OwnedInstructionSnapshot?> Rename(OwnedInstructionRenameReview review,
        Func<OwnedInstructionRenameReview, CancellationToken, Task<bool>> exactConfirmation,
        Func<OwnedInstructionSnapshot, OwnedInstructionSnapshot, bool> reconcile,
        Func<bool> eligible, CancellationToken token) =>
        admission.Run(eligible, async (request, session, current, admitted) =>
        {
            var native = RenameStore;
            if (!ReferenceEquals(review.Candidates, catalogue.Candidates))
            { throw new InvalidOperationException("Rename reference/catalogue consequences changed. Review again."); }
            if (!await exactConfirmation(review, admitted).ConfigureAwait(false)) { return null; }
            if (!ReferenceEquals(review.Candidates, catalogue.Candidates))
            { throw new InvalidOperationException("Rename reference/catalogue consequences changed during confirmation."); }
            await admission.Publish(request, session, current, static () => true, admitted).ConfigureAwait(false);
            var proposal = review.Proposal;
            var audit = new SecurityAuditEvent(request.RequestId.Value, SecurityAuditCategory.ConfigurationWrite,
                review.Recovery is null ? "definition.instruction.rename" : "definition.instruction.recover-rename",
                SecurityAuditOutcome.Requested, request.Origin == RequestOrigin.ActivatedVoice
                    ? SecurityAuditInitiator.VoiceCommand : SecurityAuditInitiator.LocalUser, review.AuditTarget);
            auditLog.Write(audit);
            var published = false;
            try
            {
                ValidateRenameNamespace(proposal);
                HoldRevision(proposal.Source.Name);
                HoldRevision(proposal.DestinationName, existingOnly: true);
                Task<OwnedInstructionSnapshot> Commit(Func<OwnedInstructionSnapshot> operation) =>
                    admission.Publish(request, session, current, () =>
                    {
                        ValidateRenameNamespace(proposal);
                        return operation();
                    }, admitted).AsTask();
                var saved = review.Recovery is null
                    ? await native.Rename(proposal, current, Commit, admitted).ConfigureAwait(false)
                    : await native.RecoverRename(review.Recovery, current, Commit, admitted).ConfigureAwait(false);
                published = true;
                await admission.Publish(request, session, current, () =>
                {
                    if (!reconcile(proposal.Source, saved))
                    { throw new IOException("Renamed source catalogue reconciliation is unconfirmed. Keep both identities held and explicitly reopen/reconcile."); }
                    return true;
                }, admitted).ConfigureAwait(false);
                auditLog.Write(audit.WithOutcome(SecurityAuditOutcome.Succeeded, "exact-rename-readback-reconciled"));
                return saved;
            }
            catch (Exception exception)
            {
                catalogue.Update(items => items.Select(item => Matches(item, proposal.Source.Name) || Matches(item, proposal.DestinationName)
                    ? item with { IsUseEnabled = false, PreservesCapturedUse = false } : item).ToArray());
                var uncertain = published || exception is InstructionRenameUnconfirmedException;
                auditLog.Write(audit.WithOutcome(uncertain ? SecurityAuditOutcome.Unknown
                    : exception is OperationCanceledException ? SecurityAuditOutcome.Cancelled : SecurityAuditOutcome.Failed,
                    uncertain ? "rename-recovery-required" : "rename-not-confirmed"));
                if (published && exception is not InstructionRenameUnconfirmedException)
                { throw new InstructionRenameUnconfirmedException(exception); }
                throw;
            }
        }, token);
}
