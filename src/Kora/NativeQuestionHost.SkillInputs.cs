using Kora.Application.Skills;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora;

internal sealed partial class NativeQuestionHost
{
    private OwnedSkillInputQuestions? skillInputQuestions;

    internal async Task<HostInteractionDecision> AskSkillInputAsync(OwnedSkillInputQuestions collection,
        QuestionSpec spec, Func<bool> admitted, Func<NativeQuestionViewModel, Task> present, CancellationToken token)
    {
        if (disposed || !canInteract() || !admitted()
            || !string.Equals(spec.Purpose, OwnedSkillInputQuestions.Purpose, StringComparison.Ordinal)
            || Interlocked.CompareExchange(ref waiting, 1, 0) != 0)
        { throw new InvalidOperationException("A single exact private question owner is required."); }
        var ownership = admissionRevision?.Invoke();
        var privacy = privateRevision?.Invoke();
        bool Eligible() => !disposed && canInteract() && admitted() && !token.IsCancellationRequested
            && time.GetUtcNow() < collection.ExpiresAt && ownership == admissionRevision?.Invoke()
            && privacy == privateRevision?.Invoke();
        using var activity = HostActivity.BeginRoot(collection.Request, HostActivityLayer.Desktop, HostOperation.Presentation);
        NativeQuestionViewModel? opened = null;
        currentAdmission = Eligible;
        skillInputQuestions = collection;
        try
        {
            var created = await questions.CreateAsync(collection.Request, spec, collection.ExpiresAt, token);
            if (!Eligible() || created is not { Outcome: HostInteractionOutcome.Presented, Question: { } initial })
            { throw new InvalidOperationException("The exact literal input question could not be presented."); }
            opened = new(initial, questions, authorization, reviews, Eligible, time, logger);
            current = opened;
            opened.BindVoiceReplies(voiceReplies, () => ownership is not null && privacy is not null
                && ReferenceEquals(current, opened) && Eligible());
            await present(opened);
            var answer = await opened.Completion.WaitAsync(token);
            activity.Complete(answer.Outcome == HostInteractionOutcome.Answered
                ? HostOperationOutcome.Completed : HostOperationOutcome.Cancelled);
            return answer;
        }
        catch (OperationCanceledException) { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch { activity.Complete(HostOperationOutcome.Failed); throw; }
        finally
        {
            opened?.Close("Literal input collection ended. No Save, Enable or Use was approved.");
#pragma warning disable VSTHRD003 // Keep the volatile answer owner alive until all reply resources and commits join.
            if (opened is not null) { await opened.OperationQuiescence; }
            if (voiceReplies is not null) { await voiceReplies.Quiescence; }
#pragma warning restore VSTHRD003
            skillInputQuestions = null;
            current = null;
            currentAdmission = null;
            Volatile.Write(ref waiting, 0);
        }
    }
}
