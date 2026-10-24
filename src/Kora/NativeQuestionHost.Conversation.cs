using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Authorization;
using Kora.Core.Network;

namespace Kora;

internal sealed partial class NativeQuestionHost
{
    internal async Task<ConversationQuestionPresentation> AskConversationAsync(HostRequest request,
        HostRevision generation, QuestionSpec spec, DateTimeOffset expiresAt, Func<bool> admitted,
        Func<NativeQuestionViewModel, Task> present, CancellationToken token,
        HostOperationProposal? webProposal = null, Uri? webAddress = null)
    {
        if (disposed || !canInteract() || !admitted() || HostActivity.RequireCurrent().Request != request
            || request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || Interlocked.CompareExchange(ref waiting, 1, 0) != 0)
        { throw new InvalidOperationException("A single exact private question presenter is required. No competing question was opened."); }
        var ownership = admissionRevision?.Invoke();
        var privacy = privateRevision?.Invoke();
        bool Eligible() => !disposed && canInteract() && admitted() && !token.IsCancellationRequested
            && time.GetUtcNow() < expiresAt && ownership == admissionRevision?.Invoke() && privacy == privateRevision?.Invoke();
        currentAdmission = Eligible;
        NativeQuestionViewModel? opened = null;
        try
        {
            var task = await store.ReadTaskAsync(request.SessionId, request.TaskId, token);
            if (!Eligible() || task is not { CurrentSource: true, Task.State: HostTaskState.DispatchRecorded, Task.Revision.Value: 2 }
                || !request.IsWithinIntent(task.Task.Request) || task.Generation != generation)
            { throw new InvalidOperationException("The original conversation task is no longer dispatched in this exact generation."); }
            if (webProposal is not null && (webAddress is null || webProposal.Request != request
                || !WebPageAccessBinding.Matches(webProposal, webAddress) || webProposal.ExpiresAt != expiresAt
                || callObservation is null))
            { throw new InvalidOperationException("The exact page proposal and current native policy observation are required."); }
            var policy = callObservation?.Invoke().Authorization(Eligible(), mandatoryGatesSatisfied: webProposal is not null)
                ?? new(Eligible(), false, false, true);
            await store.PublishTrustedSnapshotAsync(request, policy, webProposal, expectedObservationRevision: 0, token);
            var created = webProposal is null
                ? await questions.CreateAsync(request, spec, expiresAt, token)
                : await authorization.PresentWebPageOnceAsync(request, webAddress!, token);
            if (created.Outcome != HostInteractionOutcome.Presented || created.Question is not { } initial
                || initial.SessionGeneration != generation || !Eligible())
            { throw new InvalidOperationException("The exact clarification could not be durably presented."); }
            opened = new(initial, questions, authorization, reviews, Eligible, time, logger);
            current = opened;
            Interlocked.Exchange(ref currentFocusRevision, Interlocked.Read(ref focusRevision));
            opened.BindVoiceReplies(voiceReplies, () => ownership is not null && privacy is not null
                && ReferenceEquals(current, opened) && Eligible());
            await present(opened);
            var completed = await opened.Completion.WaitAsync(token);
            return new(initial, completed);
        }
        finally
        {
            opened?.Close("Original clarification presentation ended. Inspect the retained task outcome; no replay or automatic retry was admitted.");
#pragma warning disable VSTHRD003 // Joins this native owner's existing reply retirement without blocking the dispatcher.
            if (opened is not null) { await opened.OperationQuiescence; }
            if (voiceReplies is not null) { await voiceReplies.Quiescence; }
#pragma warning restore VSTHRD003
            current = null;
            currentAdmission = null;
            Volatile.Write(ref waiting, 0);
        }
    }
}
