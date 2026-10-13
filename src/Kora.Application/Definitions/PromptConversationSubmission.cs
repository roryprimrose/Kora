using System.Diagnostics;

using Kora.Application.Hosting;
using Kora.Core.Artifacts;
using Kora.Core.Conversation;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Storage;

namespace Kora.Application.Definitions;

/// <summary>Volatile exact-source review followed by one native, exact-session conversation submission.</summary>
public sealed class PromptConversationSubmission(SessionWorkspaceService workspace, SessionConversationService conversation)
{
    public async Task<SessionPage<SessionWorkspaceEntry>> ReadTargets(Guid? after, Func<bool> eligible, CancellationToken token)
    {
        Require(eligible, token);
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Storage, RequestOrigin.LocalUi);
        var page = await workspace.ReadMetadataAsync(after, 25, token).ConfigureAwait(false);
        Require(eligible, token);
        if (page.Records.IsDefault || page.Records.Length > 25)
        { throw new InvalidDataException("Refresh requires a complete bounded session metadata page."); }
        _ = SessionListSearchPage.Serialize(new(page.Records, page.Records.Length, 0, page.Next is not null, null));
        activity.Complete(HostOperationOutcome.Completed);
        return page;
    }

    public async Task<PreparedPromptConversation> Prepare(PromptParameterDraft draft, SessionWorkspaceEntry target,
        Func<ArtifactDefinition, CancellationToken, Task<bool>> fresh, Func<bool> eligible, CancellationToken token)
    {
        Require(eligible, token);
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Policy, RequestOrigin.LocalUi);
        var expanded = await draft.CaptureUse(fresh, token).ConfigureAwait(false);
        await RequireTarget(target, eligible, token).ConfigureAwait(false);
        Require(eligible, token);
        if (!draft.IsUseCurrent(expanded)) { throw new InvalidOperationException("Preview the current complete expanded request again."); }
        var cause = new ActivityLink(HostActivity.RequireCurrent().Activity!.Context);
        activity.Complete(HostOperationOutcome.Completed);
        return new(draft, expanded, target, cause);
    }

    public async Task<ConversationSubmissionReceipt> Submit(PreparedPromptConversation reviewed,
        Func<ArtifactDefinition, CancellationToken, Task<bool>> fresh, Func<bool> eligible, CancellationToken token)
    {
        Require(eligible, token);
        if (!reviewed.TryConsume()) { throw new InvalidOperationException("This submission was already attempted. Inspect its exact receipt; never replay."); }
        bool Current() => eligible() && reviewed.Draft.IsUseCurrent(reviewed.Expanded);
        async Task Revalidate(CancellationToken admitted)
        {
            Require(Current, admitted);
            await reviewed.Draft.CaptureUse(fresh, admitted).ConfigureAwait(false);
            Require(Current, admitted);
        }
        await Revalidate(token).ConfigureAwait(false);
        await RequireTarget(reviewed.Target, Current, token).ConfigureAwait(false);
        // No legacy Use, draft import, instruction intent relabelling, normalization or alternate persistence route.
        return await conversation.SubmitAsync(reviewed.Target, reviewed.Expanded.UserRequest, Current, token,
            Revalidate, [reviewed.Cause]).ConfigureAwait(false);
    }

    private async Task RequireTarget(SessionWorkspaceEntry target, Func<bool> eligible, CancellationToken token)
    {
        Require(eligible, token);
        if (!target.Authority.IsActive) { throw new InvalidOperationException("Select an existing Active session; no implicit Resume or new session."); }
        using var activity = HostActivity.BeginOperation(HostActivityLayer.Application, HostOperation.Storage, RequestOrigin.LocalUi);
        var page = await workspace.SearchListAsync(SessionListSearchKind.ExactId, SessionListFilter.Active,
            target.Authority.SessionId.Value.ToString("D"), null, 1, token).ConfigureAwait(false);
        Require(eligible, token);
        if (page.Records.Length != 1 || page.Records[0] != target)
        { throw new InvalidOperationException("The exact target changed. Refresh and explicitly select its current metadata."); }
        activity.Complete(HostOperationOutcome.Completed);
    }

    private static void Require(Func<bool> eligible, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!eligible()) { throw new InvalidOperationException("Private native prompt submission admission expired."); }
    }
}
