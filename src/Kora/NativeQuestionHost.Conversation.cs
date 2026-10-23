using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora;

internal sealed partial class NativeQuestionHost
{
    internal async Task<ConversationQuestionPresentation> AskConversationAsync(HostRequest request,
        HostRevision generation, QuestionSpec spec, DateTimeOffset expiresAt, Func<bool> admitted,
        Func<NativeQuestionViewModel, Task> present, CancellationToken token)
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
                || task.Task.Request != request || task.Generation != generation)
            { throw new InvalidOperationException("The original conversation task is no longer dispatched in this exact generation."); }
            var policy = callObservation?.Invoke().Authorization(Eligible(), mandatoryGatesSatisfied: false)
                ?? new(Eligible(), false, false, true);
            await store.PublishTrustedSnapshotAsync(request, policy, proposal: null, expectedObservationRevision: 0, token);
            var created = await questions.CreateAsync(request, spec, expiresAt, token);
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
