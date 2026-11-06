using Kora.Core.Dependencies;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora.Application.Interaction;

public sealed class HostQuestionService(IHostInteractionStore store, TimeProvider time)
{
    private readonly InteractionTransaction transaction = new(store);
    public event Action<HostQuestionKey?>? ReplyTargetChanging;

    public ValueTask<HostInteractionDecision> ReviewReplyAsync(HostQuestionKey key, HostRevision generation,
        Func<bool> isPresented, CancellationToken cancellationToken) =>
        UpdateAsync(key, "question.voice-review", (snapshot, question) =>
            question.Proposal is null && question.SessionGeneration == generation && isPresented()
                ? (snapshot, new(HostInteractionOutcome.Presented, "exact-voice-question-reviewed", question))
                : InteractionTransaction.Reject(snapshot, "voice-question-unavailable"),
            cancellationToken, retireReply: false);

    public ValueTask<HostInteractionDecision> SubmitReplyAsync(HostQuestionKey key, HostRevision generation,
        QuestionAnswer answer, Func<bool> isPresented, CancellationToken cancellationToken) =>
        UpdateAsync(key, "question.voice-submit", (snapshot, question) =>
            question.SessionGeneration == generation && isPresented()
                ? Answer(snapshot, question, answer, RequestOrigin.ActivatedVoice, submitting: true)
                : InteractionTransaction.Reject(snapshot, "voice-question-unavailable"),
            cancellationToken, retireReply: false);

    public ValueTask<HostInteractionDecision> SubmitNativeReplyAsync(HostQuestionKey key, HostRevision generation,
        QuestionAnswer answer, Func<bool> isPresented, CancellationToken cancellationToken) =>
        UpdateAsync(key, "question.submit", (snapshot, question) =>
            question.SessionGeneration == generation && isPresented()
                ? Answer(snapshot, question, answer, RequestOrigin.LocalUi, submitting: true)
                : InteractionTransaction.Reject(snapshot, "native-question-unavailable"), cancellationToken);

    public ValueTask<HostInteractionDecision> CreateAsync(HostRequest request, QuestionSpec spec,
        DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        ReplyTargetChanging?.Invoke(null);
        return transaction.RunAsync(request, "question.create", snapshot => Create(snapshot, request, spec, expiresAt), cancellationToken);
    }

    private (HostInteractionSnapshot, HostInteractionDecision) Create(HostInteractionSnapshot snapshot,
        HostRequest request, QuestionSpec spec, DateTimeOffset expiresAt)
    {
        if (!InteractionTransaction.CanInteract(snapshot) || expiresAt <= time.GetUtcNow())
        {
            return InteractionTransaction.Reject(snapshot, "question-unavailable");
        }
        var question = new HostQuestionRecord(new(request, new(Guid.NewGuid()), new(1)), spec, snapshot.Session.Generation, expiresAt);
        return (snapshot with { Questions = snapshot.Questions.Add(question) },
            new(HostInteractionOutcome.Presented, "question-presented", question));
    }

    public ValueTask<HostInteractionDecision> ReviseAsync(HostQuestionKey key, QuestionSpec spec,
        DateTimeOffset expiresAt, CancellationToken cancellationToken) =>
        UpdateAsync(key, "question.revise", (snapshot, question) => Revise(snapshot, question, spec, expiresAt), cancellationToken);

    private (HostInteractionSnapshot, HostInteractionDecision) Revise(HostInteractionSnapshot snapshot,
        HostQuestionRecord question, QuestionSpec spec, DateTimeOffset expiresAt)
    {
        if (question.Proposal is not null || expiresAt <= time.GetUtcNow())
        {
            return InteractionTransaction.Reject(snapshot, "revision-not-permitted");
        }
        return Replace(snapshot, question with { Key = question.Key.Next(), Spec = spec, ExpiresAt = expiresAt, Draft = null },
            HostInteractionOutcome.Presented, "question-revised");
    }

    public ValueTask<HostInteractionDecision> DraftAsync(HostQuestionKey key, QuestionAnswer answer,
        RequestOrigin channel, CancellationToken cancellationToken) =>
        AnswerAsync(key, answer, channel, submitting: false, cancellationToken);

    public ValueTask<HostInteractionDecision> SubmitAsync(HostQuestionKey key, QuestionAnswer answer,
        RequestOrigin channel, CancellationToken cancellationToken) =>
        AnswerAsync(key, answer, channel, submitting: true, cancellationToken);

    public ValueTask<HostInteractionDecision> CancelAsync(HostQuestionKey key, CancellationToken cancellationToken) =>
        UpdateAsync(key, "question.cancel", (snapshot, question) =>
            Replace(snapshot, question with { Key = key.Next(), Status = QuestionStatus.Cancelled },
                HostInteractionOutcome.Cancelled, "question-cancelled"), cancellationToken);

    private ValueTask<HostInteractionDecision> AnswerAsync(HostQuestionKey key, QuestionAnswer answer,
        RequestOrigin channel, bool submitting, CancellationToken cancellationToken) =>
        UpdateAsync(key, submitting ? "question.submit" : "question.draft",
            (snapshot, question) => Answer(snapshot, question, answer, channel, submitting), cancellationToken);

    private static (HostInteractionSnapshot, HostInteractionDecision) Answer(HostInteractionSnapshot snapshot,
        HostQuestionRecord question, QuestionAnswer answer, RequestOrigin channel, bool submitting)
    {
        if (channel is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || !question.Spec.Accepts(answer, submitting))
        {
            return InteractionTransaction.Reject(snapshot, "invalid-answer");
        }
        if (submitting && question.Proposal is not null)
        {
            return InteractionTransaction.Reject(snapshot, "approval-service-required");
        }
        if (question.Spec.Kind == QuestionKind.Form && !submitting)
        { return InteractionTransaction.Reject(snapshot, "form-drafts-are-volatile"); }
        if (string.Equals(question.Spec.Purpose, LocalModelQuestionPolicy.Purpose, StringComparison.Ordinal)
            && string.Equals(question.Spec.SourceId, LocalModelQuestionPolicy.SourceId, StringComparison.Ordinal))
        {
            try { LocalModelQuestionPolicy.RequireAnswerContent(answer); }
            catch (Exception exception) when (exception is InvalidDataException or Kora.Core.Conversation.ConversationCredentialException)
            { return InteractionTransaction.Reject(snapshot, "clarification-content-refused-before-storage"); }
        }
        return Replace(snapshot, question with
        {
            Key = question.Key.Next(),
            Draft = question.Spec.CanonicalAnswer(answer),
            AnswerChannel = channel,
            Status = submitting ? QuestionStatus.Answered : QuestionStatus.Pending,
        }, submitting ? HostInteractionOutcome.Answered : HostInteractionOutcome.Drafted, "answer-recorded");
    }

    private ValueTask<HostInteractionDecision> UpdateAsync(HostQuestionKey key, string action,
        Func<HostInteractionSnapshot, HostQuestionRecord, (HostInteractionSnapshot, HostInteractionDecision)> update,
        CancellationToken cancellationToken, bool retireReply = true)
    {
        if (retireReply) { ReplyTargetChanging?.Invoke(key); }
        return transaction.RunAsync(key.Request, action, snapshot => Update(snapshot, key, update), cancellationToken);
    }

    private (HostInteractionSnapshot, HostInteractionDecision) Update(HostInteractionSnapshot snapshot, HostQuestionKey key,
        Func<HostInteractionSnapshot, HostQuestionRecord, (HostInteractionSnapshot, HostInteractionDecision)> update)
    {
        var index = InteractionTransaction.FindQuestion(snapshot, key);
        if (!InteractionTransaction.CanInteract(snapshot) || index < 0)
        {
            return InteractionTransaction.Reject(snapshot, "question-conflict", HostInteractionOutcome.Conflict);
        }
        var question = snapshot.Questions[index];
        if (question.Status != QuestionStatus.Pending || question.SessionGeneration != snapshot.Session.Generation)
        {
            return InteractionTransaction.Reject(snapshot, "question-closed", HostInteractionOutcome.Conflict);
        }
        if (question.ExpiresAt <= time.GetUtcNow())
        {
            return Replace(snapshot, question with { Key = key.Next(), Status = QuestionStatus.Expired },
                HostInteractionOutcome.Expired, "question-expired");
        }
        return update(snapshot, question);
    }

    internal static (HostInteractionSnapshot, HostInteractionDecision) Replace(HostInteractionSnapshot snapshot,
        HostQuestionRecord question, HostInteractionOutcome outcome, string reason)
    {
        var index = snapshot.Questions.FindIndex(item => item.Key.QuestionId == question.Key.QuestionId);
        return (snapshot with { Questions = snapshot.Questions.SetItem(index, question) }, new(outcome, reason, question));
    }
}