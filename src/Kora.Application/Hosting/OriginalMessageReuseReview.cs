using System.Globalization;

using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Presentation;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>One-shot private original-text handoff, not replay, resumption or submission authority.</summary>
public sealed class OriginalMessageReuseReview : IDisposable
{
    private readonly SessionWorkspaceService workspace;
    private readonly ISessionWorkspaceAccess access;
    private readonly NewRequestComposer composer;
    private readonly long control;
    private readonly long bufferEpoch;
    private readonly HostId<SessionIdentity> sourceSession;
    private Func<bool> admitted;
    private SessionWorkspaceEntry? source;
    private AdmittedDetailContent? content;
    private string previousBody;
    private readonly bool replacement;
    private bool confirming;
    private bool handingOff;
    private bool retired;

    private OriginalMessageReuseReview(SessionWorkspaceService workspace, ISessionWorkspaceAccess access,
        NewRequestComposer composer, SessionWorkspaceEntry source, AdmittedDetailContent content,
        Func<bool> admitted, long control, long bufferEpoch)
    {
        this.workspace = workspace;
        this.access = access;
        this.composer = composer;
        this.source = source;
        this.content = content;
        this.admitted = admitted;
        this.control = control;
        this.bufferEpoch = bufferEpoch;
        sourceSession = source.Authority.SessionId;
        previousBody = composer.Body;
        replacement = composer.Dirty;
        workspace.SessionLifecycleChanged += Revoke;
        workspace.SessionRetired += Revoke;
    }

    public event EventHandler? Retired;
    public bool Available
    {
        get
        {
            if (SourceCurrent() && (handingOff || composer.IsReuseBufferCurrent(bufferEpoch))) { return true; }
            Dispose();
            return false;
        }
    }
    public string Body => Available ? content!.Source : string.Empty;
    public string PreviousBody => Available ? previousBody : string.Empty;
    public bool RequiresReplacement => Available && replacement;
    public string Attribution => Available
        ? "Historical retained original USER text only; copying content does not resume or replay its task or HOST outcome."
            + "\nSource session " + source!.Authority.SessionId.Value.ToString("D")
            + " | current generation " + source.Authority.Generation.Value.ToString(CultureInfo.InvariantCulture)
            + " | metadata revision " + (source.Metadata?.Revision.Value ?? 0).ToString(CultureInfo.InvariantCulture)
            + " | " + (source.Authority.IsActive ? "Active" : "Done")
            + "\nOriginal request " + content!.ConversationMessage!.Request.RequestId.Value.ToString("D")
            + " | original task " + content.ConversationMessage.Request.TaskId.Value.ToString("D")
            + "\nUSER sequence " + content.ConversationMessage.Sequence.ToString(CultureInfo.InvariantCulture)
            + " | original submitted generation " + content.ConversationMessage.Generation.Value.ToString(CultureInfo.InvariantCulture)
            + " | immutable record revision " + content.Reference.Revision.ToString(CultureInfo.InvariantCulture)
            + "\nSubmitted UTC " + content.ConversationMessage.SubmittedAt.ToString("O", CultureInfo.InvariantCulture)
            + "\nConfirm copies the complete exact body to a private New request memory buffer only. "
            + "Create, reviewed Save and reviewed Submit remain separate decisions with fresh host identities. "
            + "No clipboard, question answer, approval, provider operation or activity renewal."
        : string.Empty;

    public static async Task<OriginalMessageReuseReview> ReviewAsync(SessionWorkspaceService workspace,
        ISessionWorkspaceAccess access, SessionWorkspaceEntry source, ConversationMessage original,
        NewRequestComposer composer, Func<bool> admitted, CancellationToken token)
    {
        var controlRevision = access.ControlRevision;
        var epoch = composer.ReuseBufferEpoch;
        bool Current() => access.CanInspect && access.CanControl && access.ControlRevision == controlRevision
            && composer.IsReuseBufferCurrent(epoch) && admitted();
        if (!Current()) { throw new OperationCanceledException("Original-text reuse requires current private native admission and an uncreated New request."); }
        ConversationText.Validate(composer.Body);
        var originalContent = await workspace.ReadConversationDetailAsync(source, original, Current, token);
        token.ThrowIfCancellationRequested();
        if (!Current()) { throw new OperationCanceledException("Original-text reuse admission changed during review."); }
        return new(workspace, access, composer, source, originalContent, admitted, controlRevision, epoch);
    }

    public async Task<bool> ConfirmAsync(bool replaceDirtyBuffer, CancellationToken token)
    {
        if (confirming || !Available) { Dispose(); return false; }
        if (replacement && !replaceDirtyBuffer) { Dispose(); return false; }
        confirming = true;
        try
        {
            var frozen = content!;
            _ = await workspace.ReadConversationDetailAsync(source!, frozen.ConversationMessage!,
                () => Available, token);
            token.ThrowIfCancellationRequested();
            if (!Available) { return false; }
            // The source fence remains live through synchronous binding/focus callbacks; only a completed
            // handoff becomes independent memory content. No historical request identity is transferred.
            handingOff = true;
            return composer.SeedOriginal(bufferEpoch, frozen.Source,
                () => !token.IsCancellationRequested && SourceCurrent());
        }
        finally { Dispose(); }
    }

    private bool SourceCurrent() => !retired && access.CanInspect && access.CanControl
        && access.ControlRevision == control && admitted();

    private void Revoke(HostId<SessionIdentity> session)
    {
        if (sourceSession == session) { Dispose(); }
    }

    public void Dispose()
    {
        if (retired) { return; }
        retired = true;
        workspace.SessionLifecycleChanged -= Revoke;
        workspace.SessionRetired -= Revoke;
        source = null;
        content = null;
        previousBody = string.Empty;
        admitted = static () => false;
        var notify = Retired;
        Retired = null;
        notify?.Invoke(this, EventArgs.Empty);
    }
}
