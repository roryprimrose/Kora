using Kora.Application.Hosting;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora;

internal sealed partial class NativeQuestionHost
{
    internal async Task<bool> AskApplicationLifecycleAsync(HostRequest request, ApplicationLifecycleOperation operation,
        string prompt, DateTimeOffset expiresAt,
        Func<bool> admitted, HostTaskCoordinator tasks, Func<NativeQuestionViewModel, Task> present, CancellationToken token)
    {
        if (disposed || !canInteract() || !admitted()
            || request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || Interlocked.CompareExchange(ref waiting, 1, 0) != 0)
        { throw new InvalidOperationException("The private native question owner is unavailable. No submitted work was stopped."); }
        var ownership = admissionRevision?.Invoke();
        var privacy = privateRevision?.Invoke();
        bool Eligible() => !disposed && canInteract() && admitted() && !token.IsCancellationRequested
            && ownership == admissionRevision?.Invoke() && privacy == privateRevision?.Invoke()
            && time.GetUtcNow() < expiresAt;
        using var activity = HostActivity.BeginRoot(request, HostActivityLayer.Desktop, HostOperation.Request);
        currentAdmission = Eligible;
        NativeQuestionViewModel? opened = null;
        try
        {
            var intent = await store.RecordControlIntentAsync(request, token);
            var policy = callObservation?.Invoke().Authorization(Eligible(), mandatoryGatesSatisfied: false)
                ?? new(Eligible(), false, false, true);
            await store.PublishTrustedSnapshotAsync(request, policy, null, expectedObservationRevision: 0, token);
            var spec = new QuestionSpec(prompt, QuestionKind.SingleChoice,
                [new("confirm", operation == ApplicationLifecycleOperation.StopAllWork
                    ? "Confirm: stop reviewed requests, join resources, keep Kora open"
                    : "Confirm: stop reviewed requests, join resources, then continue"),
                    new("cancel", "Cancel: keep submitted requests unchanged")],
                purpose: operation == ApplicationLifecycleOperation.StopAllWork ? "stop-all-work" : "application-lifecycle",
                sourceId: operation == ApplicationLifecycleOperation.StopAllWork ? "host-stop-all-work" : "host-application-lifecycle");
            var created = await questions.CreateAsync(request, spec, expiresAt, token);
            if (!Eligible() || created is not { Outcome: HostInteractionOutcome.Presented, Question: { } initial })
            { throw new InvalidOperationException("Application lifecycle confirmation could not be durably presented."); }
            opened = new(initial, questions, authorization, reviews, Eligible, time, logger);
            current = opened;
            Interlocked.Exchange(ref currentFocusRevision, Interlocked.Read(ref focusRevision));
            opened.BindVoiceReplies(voiceReplies, () => ownership is not null && privacy is not null
                && ReferenceEquals(current, opened) && Eligible());
            await present(opened);
            var decision = await opened.Completion.WaitAsync(token);
            var confirmed = Eligible() && decision is { Outcome: HostInteractionOutcome.Answered, Question: { } answer }
                && answer.AnswerChannel == request.Origin && answer.Draft?.Choices is ["confirm"];
            var terminal = await tasks.RecordOutcomeAsync(intent,
                confirmed ? HostTaskState.Succeeded : HostTaskState.Cancelled, CancellationToken.None);
            var readback = await store.ReadTaskAsync(request.SessionId, request.TaskId, token);
            if (readback?.Task != terminal)
            { throw new InvalidOperationException("The lifecycle decision terminal readback is unconfirmed."); }
            activity.Complete(confirmed ? HostOperationOutcome.Completed : HostOperationOutcome.Cancelled);
            return confirmed && Eligible();
        }
        finally
        {
            opened?.Close("Application lifecycle review ended. No prior answer can authorize a later attempt.");
#pragma warning disable VSTHRD003 // The native question owner retains reply resources until retirement actually joins.
            if (opened is not null) { await opened.OperationQuiescence; }
            if (voiceReplies is not null) { await voiceReplies.Quiescence; }
#pragma warning restore VSTHRD003
            current = null;
            currentAdmission = null;
            Volatile.Write(ref waiting, 0);
        }
    }
}
