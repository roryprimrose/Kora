using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.Interaction.ConversationNotices;

/// <summary>Content-free projection of an audited current-run conversation source, not continuation authority.</summary>
public sealed record ConversationNoticeAuthority(
    HostRequest Request, HostRevision Generation, long MessageRevision,
    HostId<ModelTurnIdentity> Turn, DateTimeOffset SubmittedAt,
    ModelTurnOutcome? Outcome, ModelTurnReason? Reason,
    HostQuestionKey? Question, DateTimeOffset? QuestionExpiresAt)
{
    public const string Source = "conversation.user-request.v1";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(30);

    public void Validate(HostTaskRecord task)
    {
        Turn.Validate();
        if (Request != task.Request || Request.InvocationId is not null
            || Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || Generation.Value <= 0 || MessageRevision != task.Revision.Value || MessageRevision < 2
            || SubmittedAt.Offset != TimeSpan.Zero || SubmittedAt > DateTimeOffset.MaxValue - Lifetime
            || (Outcome is null) != (Reason is null)
            || Outcome is { } outcome && !Enum.IsDefined(outcome)
            || Reason is { } reason && !Enum.IsDefined(reason)
            || Reason == ModelTurnReason.HostInterrupted
            || (Question is null) != (QuestionExpiresAt is null))
        { throw new InvalidDataException("Conversation notice source lineage or terminal metadata is invalid."); }
        if (Question is { } question)
        {
            var expiry = QuestionExpiresAt!.Value;
            if (Outcome is not null || MessageRevision != 2 || task.State != HostTaskState.DispatchRecorded
                || question.Request != Request
                || expiry.Offset != TimeSpan.Zero || expiry <= SubmittedAt
                || expiry > SubmittedAt + Lifetime)
            { throw new InvalidDataException("Conversation notice question is not an exact admitted clarification."); }
        }
        else
        {
            var expected = Outcome switch
            {
                ModelTurnOutcome.Succeeded => HostTaskState.Succeeded,
                ModelTurnOutcome.Cancelled or ModelTurnOutcome.TimedOut => HostTaskState.Unknown,
                ModelTurnOutcome.Unavailable => HostTaskState.Unavailable,
                ModelTurnOutcome.Unknown => HostTaskState.Unknown,
                _ => HostTaskState.Denied,
            };
            if (Outcome is null || MessageRevision < 3 || task.State != expected)
            { throw new InvalidDataException("Conversation notice terminal does not match its durable task receipt."); }
        }
    }
}
