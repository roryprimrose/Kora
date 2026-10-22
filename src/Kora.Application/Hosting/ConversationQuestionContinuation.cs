using Kora.Application.Interaction;
using Kora.Core.Dependencies;
using Kora.Core.Diagnostics;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora.Application.Hosting;

/// <summary>One live exact-task clarification, never a replayable approval or persisted continuation.</summary>
public sealed class ConversationQuestionContinuation(
    IHostInteractionStore store, ConversationQuestionPresenter presenter,
    HostRequest request, HostRevision generation, DateTimeOffset expiresAt,
    Func<bool> admitted, TimeProvider time)
{
    private int used;

    internal async Task<ConversationQuestionReply> AskAsync(LocalModelQuestion question, CancellationToken token)
    {
        if (Interlocked.Exchange(ref used, 1) != 0)
        { throw new InvalidOperationException("This exact task's one clarification was already consumed. Start a fresh request."); }
        RequireLive(token);
        var spec = LocalModelQuestionPolicy.CreateSpec(question);
        using var activity = HostActivity.BeginChild(HostActivityLayer.Application, HostOperation.Presentation);
        try
        {
            var presented = await presenter(request, generation, spec, expiresAt, admitted, token).ConfigureAwait(false);
            RequireLive(token);
            var initial = presented.Presented;
            var completed = presented.Completed;
            if (initial.Key.Request != request || initial.Key.Revision.Value != 1
                || initial.SessionGeneration != generation || initial.Status != QuestionStatus.Pending
                || initial.Proposal is not null || initial.Draft is not null || initial.AnswerChannel is not null
                || initial.ExpiresAt != expiresAt || !SameSpec(initial.Spec, spec)
                || completed.Outcome != HostInteractionOutcome.Answered
                || completed.Question is not { Status: QuestionStatus.Answered, Proposal: null, Draft: { } answer } answered
                || answered.Key.Request != request || answered.Key.QuestionId != initial.Key.QuestionId
                || answered.Key.Revision.Value <= initial.Key.Revision.Value
                || answered.SessionGeneration != generation || answered.ExpiresAt != expiresAt
                || !SameSpec(answered.Spec, spec)
                || answered.AnswerChannel is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
                || !spec.Accepts(answer, submitting: true))
            { throw new InvalidDataException("Clarification was not freshly answered for this exact original task. Inspect status; do not replay."); }
            LocalModelQuestionPolicy.RequireAnswerContent(answer);
            var transaction = new InteractionTransaction(store);
            var observed = await transaction.RunAsync(request, "question.continue", snapshot =>
            {
                RequireLive(token);
                var canonical = snapshot.Questions.SingleOrDefault(item => item.Key == answered.Key);
                return InteractionTransaction.CanInteract(snapshot) && snapshot.Session.Generation == generation
                    && canonical is not null && SameRecord(canonical, answered)
                    ? (snapshot, new(HostInteractionOutcome.Answered, "exact-conversation-question-consumed", canonical))
                    : InteractionTransaction.Reject(snapshot, "conversation-question-no-longer-current");
            }, token).ConfigureAwait(false);
            RequireLive(token);
            if (observed.Outcome != HostInteractionOutcome.Answered || observed.Question is null)
            { throw new InvalidOperationException("The exact clarification answer is stale or unavailable. Inspect status; start a fresh request."); }
            activity.Complete(HostOperationOutcome.Completed);
            return new(observed.Question);
        }
        catch (OperationCanceledException)
        { activity.Complete(HostOperationOutcome.Cancelled); throw; }
        catch
        { activity.Complete(HostOperationOutcome.Failed); throw; }
    }

    private void RequireLive(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var current = HostActivity.RequireCurrent();
        if (current.Request != request || current.Activity!.IsStopped || current.Outcome != HostOperationOutcome.Unknown
            || !admitted() || time.GetUtcNow() >= expiresAt)
        { throw new InvalidOperationException("Original question ownership, correlation or expiry changed. Start a fresh request."); }
    }

    private static bool SameSpec(QuestionSpec left, QuestionSpec right) =>
        string.Equals(left.Text, right.Text, StringComparison.Ordinal) && left.Kind == right.Kind
        && string.Equals(left.Purpose, right.Purpose, StringComparison.Ordinal)
        && string.Equals(left.SourceId, right.SourceId, StringComparison.Ordinal)
        && left.Minimum == right.Minimum && left.Maximum == right.Maximum && left.MaximumTextLength == right.MaximumTextLength
        && left.Options.SequenceEqual(right.Options);

    private static bool SameRecord(HostQuestionRecord left, HostQuestionRecord right) =>
        left.Key == right.Key && left.Status == right.Status && left.SessionGeneration == right.SessionGeneration
        && left.ExpiresAt == right.ExpiresAt && left.Proposal is null && right.Proposal is null
        && left.AnswerChannel == right.AnswerChannel && SameSpec(left.Spec, right.Spec)
        && left.Draft is { } a && right.Draft is { } b
        && string.Equals(a.Text, b.Text, StringComparison.Ordinal) && a.Choices.SequenceEqual(b.Choices);
}
