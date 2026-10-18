using Kora.Core.Artifacts;

using Microsoft.Extensions.Logging;

namespace Kora.Application.Definitions;

public sealed partial class OwnedInstructionEditorActions
{
    public async Task<OwnedDraftRebase?> RebaseDraft(string name, OwnedInstructionSnapshot original, string draft,
        Func<OwnedDraftRebase, CancellationToken, Task<bool>> exactReview, ILogger diagnosticLogger,
        Func<bool> eligible, CancellationToken token)
    {
        // The owning parser validates identity/version/structure; the raw draft need not be valid.
        RequireRebaseSource(name, original);
        var candidates = catalogue.Candidates;
        var failureRevision = catalogue.ReloadFailureRevision;
        bool Current() => eligible() && !catalogue.IsReloadUnavailable
            && ReferenceEquals(candidates, catalogue.Candidates) && failureRevision == catalogue.ReloadFailureRevision;
        var result = await admission.Run<OwnedDraftRebase?>(Current, async (request, session, current, admitted) =>
        {
            var latest = await store.Read(name, admitted).ConfigureAwait(false)
                ?? throw new InvalidOperationException("The latest owned source is missing. Rebase cannot adopt a deleted source; the baseline and draft are retained.");
            RequireRebaseSource(name, latest);
            if (!string.Equals(latest.Document.Definition.Id, original.Document.Definition.Id, StringComparison.Ordinal))
            { throw new InvalidDataException("The latest source artifact ID does not match the frozen original."); }
            var proposal = await admission.Publish(request, session, current, () =>
                new OwnedDraftRebase(original, latest, new OwnedDraftComparison(latest, draft,
                    ObserveDraftStructure(name, latest, draft, diagnosticLogger, candidates, failureRevision, unavailable: false))),
                admitted).ConfigureAwait(false);
            if (!await exactReview(proposal, admitted).ConfigureAwait(false)) { return null; }
            await admission.Publish(request, session, current, static () => true, admitted).ConfigureAwait(false);
            var confirmed = await store.Read(name, admitted).ConfigureAwait(false);
            if (confirmed is null)
            { throw new InvalidOperationException("The source was deleted during Rebase review. The baseline and draft are retained."); }
            RequireRebaseSource(name, confirmed);
            if (!string.Equals(confirmed.Revision, latest.Revision, StringComparison.Ordinal))
            { throw new InvalidOperationException("The exact source revision changed during Rebase review. Explicitly Rebase again; the baseline and draft are retained."); }
            return await admission.Publish<OwnedDraftRebase?>(request, session, current, () => proposal, admitted).ConfigureAwait(false);
        }, token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (!Current()) { throw new InvalidOperationException("The Rebase observation expired before delivery. The baseline and draft are retained."); }
        return result;
    }

    private void RequireRebaseSource(string name, OwnedInstructionSnapshot snapshot)
    {
        _ = OwnedInstructionRevision.Validate(snapshot.Revision);
        _ = PrepareSave(name, snapshot, snapshot.Document.Text);
        if (!string.Equals(snapshot.Document.Definition.OwnedInstructionName, name, StringComparison.Ordinal))
        { throw new InvalidDataException("Rebase requires the exact owned source artifact identity."); }
    }
}
