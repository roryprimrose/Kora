using Kora.Core.Artifacts;
using Kora.Core.Auditing;
using Kora.Core.Authorization;
using Kora.Core.Conversation;
using Kora.Core.Hosting;
using Kora.Core.Interaction;
using Kora.Core.Storage;

namespace Kora.Application.Skills;

/// <summary>One host-issued field question over a real committed control intent. Answer content is volatile only.</summary>
public sealed class OwnedSkillInputQuestions : IHostInteractionStore, IDisposable
{
    public const string Purpose = "owned-skill-literal-input";
    private readonly Lock gate = new();
    private readonly QuestionSpec spec;
    private readonly TimeProvider time;
    private readonly Func<CancellationToken, Task> validate;
    private readonly Func<Func<HostInteractionDecision>, CancellationToken, ValueTask<HostInteractionDecision>> commit;
    private readonly ISecurityAuditLog audit;
    private HostInteractionSnapshot? snapshot;
    private HostQuestionRecord? accepted;

    internal OwnedSkillInputQuestions(HostRequest request, WorkSessionAuthorization session, HostTaskRecord intent,
        QuestionSpec spec, DateTimeOffset expiresAt, TimeProvider time, Func<CancellationToken, Task> validate,
        Func<Func<HostInteractionDecision>, CancellationToken, ValueTask<HostInteractionDecision>> commit,
        ISecurityAuditLog audit)
    {
        Request = request;
        ExpiresAt = expiresAt;
        this.spec = spec;
        this.time = time;
        this.validate = validate;
        this.commit = commit;
        this.audit = audit;
        snapshot = new(intent, session, new(true, false, false, false), null, [], []);
    }

    public HostRequest Request { get; }
    public DateTimeOffset ExpiresAt { get; }

    public async ValueTask<HostInteractionDecision> TransactAsync(HostRequest request,
        Func<HostInteractionSnapshot, HostInteractionCommit> transition, CancellationToken cancellationToken)
    {
        if (request != Request) { throw new InvalidOperationException("A foreign question cannot use this collection."); }
        await validate(cancellationToken).ConfigureAwait(false);
        return await commit(() =>
        {
            lock (gate)
            {
                var before = snapshot ?? throw new InvalidOperationException("The literal input question was retired.");
                if (time.GetUtcNow() >= ExpiresAt) { throw new InvalidOperationException("The literal input question expired."); }
                var next = transition(before);
                if (next.Snapshot with { Questions = before.Questions } != before
                    || next.Snapshot.Questions.Length != 1
                    || next.Snapshot.Questions[0] is not { } question
                    || !ReferenceEquals(question.Spec, spec) || question.Key.Request != Request
                    || question.ExpiresAt != ExpiresAt || question.Proposal is not null
                    || question.SessionGeneration != before.Session.Generation
                    || before.Questions is [var previous] && (question.Key.QuestionId != previous.Key.QuestionId
                        || question.Key != previous.Key && question.Key != previous.Key.Next()))
                { throw new InvalidDataException("The exact host-issued input question schema is required."); }
                if (question.Status == QuestionStatus.Answered && question.Draft is null)
                { throw new InvalidDataException("An answered literal field requires its exact data."); }
                if (question.Draft is { } answer)
                {
                    if (question.AnswerChannel != Request.Origin || !spec.Accepts(answer, submitting: true))
                    { throw new InvalidDataException("Answer only the exact question through its original input channel."); }
                    RequireValue(answer.Text!);
                }
                audit.Write(next.Audit);
                snapshot = next.Snapshot;
                accepted = next.Decision.Outcome == HostInteractionOutcome.Answered ? question : null;
                return next.Decision;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    internal string Accept(HostInteractionDecision decision)
    {
        lock (gate)
        {
            if (decision.Outcome != HostInteractionOutcome.Answered || accepted is null
                || !ReferenceEquals(decision.Question, accepted))
            { throw new InvalidOperationException("The exact field question was not answered. No value was bound."); }
            var value = accepted.Draft!.Text!;
            accepted = null;
            return value;
        }
    }

    internal static void RequireValue(string value)
    {
        OwnedPromptTemplate.ValidateValue(value);
        ConversationText.Validate(value, submission: true);
    }

    public void Dispose()
    {
        lock (gate) { snapshot = null; accepted = null; }
    }
}
