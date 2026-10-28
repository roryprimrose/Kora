using Kora.Application.Context;
using Kora.Core.Authorization;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora;

internal sealed partial class NativeQuestionHost
{
    private static readonly QuestionSpec ClipboardPermissionSpec = new(
        "Allow the built-in clipboard tool to explain explicitly reviewed snapshots using a local-only model? "
        + "You are responsible for sensitive material you place in the clipboard. Kora does not classify or scan it. "
        + "Once permits this request; This session and Always permit subsequent explicitly reviewed requests within that scope. "
        + "Cancel question denies this request without creating a grant. "
        + "Each explanation still requires its own exact-snapshot model confirmation. No hosted model, tools, speech or history.",
        QuestionKind.SingleChoice, [new("once", "Once"), new("session", "This session"), new("perpetual", "Always")],
        purpose: "authorization", sourceId: "clipboard-local-explanation");

    private static readonly QuestionSpec ClipboardReadPermissionSpec = new(
        "Allow the built-in clipboard tool to read one current plain-text snapshot into a volatile native preview? "
        + "You are responsible for sensitive material you place in the clipboard. Kora does not classify or scan it. "
        + "Once permits this read; This session and Always permit subsequent explicitly requested reads within that scope. "
        + "Cancel question denies this request without reading the clipboard or creating a grant. "
        + "This does not authorize model use, writing, speech or history.",
        QuestionKind.SingleChoice, [new("once", "Once"), new("session", "This session"), new("perpetual", "Always")],
        purpose: "authorization", sourceId: "clipboard-read");

    internal async Task<HostInteractionDecision> RequestClipboardPermissionAsync(
        HostOperationProposal proposal, HostRevision generation, Func<bool> admitted,
        Func<NativeQuestionViewModel, Task> present, CancellationToken token)
    {
        var request = proposal.Request;
        var read = proposal.Binding == ClipboardPermissionSourceUsePolicy.CreateReadBinding();
        if (disposed || !canInteract() || !admitted() || HostActivity.RequireCurrent().Request != request
            || !Kora.Core.Context.ClipboardCommand.IsDeliberateOrigin(request.Origin)
            || (!read && proposal.Binding != ClipboardPermissionSourceUsePolicy.CreateBinding())
            || proposal.Effect != HostOperationEffect.BoundedRead || callObservation is null
            || Interlocked.CompareExchange(ref waiting, 1, 0) != 0)
        { throw new InvalidOperationException("The exact native clipboard permission presenter is unavailable."); }
        var ownership = admissionRevision?.Invoke();
        var privacy = privateRevision?.Invoke();
        bool Eligible() => !disposed && canInteract() && admitted() && !token.IsCancellationRequested
            && time.GetUtcNow() < proposal.ExpiresAt
            && ownership == admissionRevision?.Invoke() && privacy == privateRevision?.Invoke();
        currentAdmission = Eligible;
        NativeQuestionViewModel? opened = null;
        try
        {
            var task = await store.ReadTaskAsync(request.SessionId, request.TaskId, token);
            if (!Eligible() || task is not { CurrentSource: true, Task.IsTerminal: false }
                || !request.IsWithinIntent(task.Task.Request) || task.Generation != generation)
            { throw new InvalidOperationException("The exact clipboard permission task is no longer current."); }
            await store.PublishTrustedSnapshotAsync(request,
                callObservation().Authorization(Eligible(), mandatoryGatesSatisfied: true),
                proposal, expectedObservationRevision: 0, token);
            var decision = await authorization.RequestGrantAsync(request, read ? ClipboardReadPermissionSpec : ClipboardPermissionSpec, token);
            if (decision.Outcome != HostInteractionOutcome.Presented) { return decision; }
            if (decision.Question is not { } initial || initial.SessionGeneration != generation || !Eligible())
            { throw new InvalidOperationException("The clipboard permission question was not durably presented."); }
            opened = new(initial, questions, authorization, reviews, Eligible, time, logger);
            current = opened;
            Interlocked.Exchange(ref currentFocusRevision, Interlocked.Read(ref focusRevision));
            await present(opened);
            var remaining = proposal.ExpiresAt - time.GetUtcNow();
            using var deadline = new CancellationTokenSource(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, time);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
            var completed = await opened.Completion.WaitAsync(cancellation.Token);
            if (!Eligible() || completed is not { Outcome: HostInteractionOutcome.Approved, Grant: { } grant })
            { return new(HostInteractionOutcome.Denied, "clipboard-permission-not-approved"); }
            return await authorization.ConsumeAsync(request, grant.Id, grant.Revision, token);
        }
        finally
        {
            opened?.Close("Clipboard permission review ended. No retained question can replay this request.");
#pragma warning disable VSTHRD003 // Joins this native owner's existing reply retirement.
            if (opened is not null) { await opened.OperationQuiescence; }
#pragma warning restore VSTHRD003
            current = null;
            currentAdmission = null;
            Volatile.Write(ref waiting, 0);
        }
    }
}
