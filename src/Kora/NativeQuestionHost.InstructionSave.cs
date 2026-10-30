using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora;

internal sealed partial class NativeQuestionHost
{
    internal async Task<bool> AskInstructionSaveAsync(HostRequest request, QuestionSpec spec, DateTimeOffset expiresAt,
        Func<bool> admitted, Func<NativeQuestionViewModel, Task> present, CancellationToken token)
    {
        if (disposed || !canInteract() || !admitted() || HostActivity.RequireCurrent().Request != request
            || request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || !string.Equals(spec.Purpose, "owned-instruction-save", StringComparison.Ordinal)
            || !string.Equals(spec.SourceId, "host-owned-instruction-editor", StringComparison.Ordinal)
            || callObservation is null || callObservation().IsProtected
            || Interlocked.CompareExchange(ref waiting, 1, 0) != 0)
        { throw new InvalidOperationException("The original exact instruction save question owner is unavailable."); }
        var ownership = admissionRevision?.Invoke();
        var privacy = privateRevision?.Invoke();
        bool Eligible() => !disposed && canInteract() && admitted() && !token.IsCancellationRequested
            && ownership is not null && privacy is not null
            && ownership == admissionRevision?.Invoke() && privacy == privateRevision?.Invoke()
            && time.GetUtcNow() < expiresAt && !callObservation().IsProtected;
        currentAdmission = Eligible;
        NativeQuestionViewModel? opened = null;
        try
        {
            var session = await store.ReadSessionAsync(request.SessionId, token);
            if (session is not { IsActive: true } || !Eligible())
            { throw new InvalidOperationException("The original instruction session is no longer active."); }
            await store.WithInstructionSession(request, session.Generation, () =>
            {
                if (!Eligible()) { throw new InvalidOperationException("The reviewed instruction save was withdrawn."); }
                return true;
            }, token);
            await store.PublishTrustedSnapshotAsync(request,
                callObservation().Authorization(Eligible(), mandatoryGatesSatisfied: false), null, 0, token);
            var created = await questions.CreateAsync(request, spec, expiresAt, token);
            if (!Eligible() || created is not { Outcome: HostInteractionOutcome.Presented, Question: { } initial }
                || initial.SessionGeneration != session.Generation)
            { throw new InvalidOperationException("The exact instruction save question was not durably presented."); }
            opened = new(initial, questions, authorization, reviews, Eligible, time, logger);
            current = opened;
            Interlocked.Exchange(ref currentFocusRevision, Interlocked.Read(ref focusRevision));
            opened.BindVoiceReplies(voiceReplies, () => ReferenceEquals(current, opened) && Eligible());
            await present(opened);
            var remaining = expiresAt - time.GetUtcNow();
            using var deadline = new CancellationTokenSource(remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero, time);
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
            var decision = await opened.Completion.WaitAsync(lifetime.Token);
            return Eligible() && decision is { Outcome: HostInteractionOutcome.Answered, Question: { } answer }
                && answer.Key == initial.Key.Next() && answer.SessionGeneration == initial.SessionGeneration
                && answer.AnswerChannel is RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice
                && answer.Draft?.Choices is ["save"];
        }
        finally
        {
            opened?.Close("Exact save review ended. No prior answer can save another revision.");
#pragma warning disable VSTHRD003 // The shared question owner joins its exact native/reply operations before releasing ownership.
            if (opened is not null) { await opened.OperationQuiescence; }
            if (voiceReplies is not null) { await voiceReplies.Quiescence; }
#pragma warning restore VSTHRD003
            current = null;
            currentAdmission = null;
            Volatile.Write(ref waiting, 0);
        }
    }
}
