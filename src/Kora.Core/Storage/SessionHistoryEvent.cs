using System.Collections.Immutable;

using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.Storage;

/// <summary>Immutable citation to a host-committed observation, never a command or replay token.</summary>
public sealed record SessionHistoryEvent(
    Guid Id, HostId<SessionIdentity> SessionId, long Sequence, HostRevision Generation,
    SessionHistoryKind Kind, SessionHistoryAvailability Availability,
    Guid? RequestId, Guid? TaskId, Guid? SourceId, long SourceRevision,
    string ProvenanceDigest, long? AuditSequence, bool Baseline,
    string? Question, ImmutableArray<QuestionOption> Options, string? Answer,
    ImmutableArray<string> Choices, HostTaskState? TaskState, HostInteractionOutcome? Decision,
    QuestionStatus? QuestionStatus, RequestOrigin? AnswerChannel)
{
    public void Validate()
    {
        SessionId.Validate();
        if (Id == Guid.Empty || Sequence <= 0 || SourceRevision < 0 || AuditSequence is <= 0
            || Generation.Value <= 0 || !Enum.IsDefined(Kind) || !Enum.IsDefined(Availability)
            || TaskState is { } state && !Enum.IsDefined(state)
            || Decision is { } decision && !Enum.IsDefined(decision)
            || QuestionStatus is { } status && !Enum.IsDefined(status)
            || AnswerChannel is { } channel && channel is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice)
            || Options.IsDefault || Choices.IsDefault
            || RequestId == Guid.Empty || TaskId == Guid.Empty || SourceId == Guid.Empty
            || ProvenanceDigest is null || ProvenanceDigest.Length != 64 || !ProvenanceDigest.All(char.IsAsciiHexDigit))
        {
            throw new InvalidDataException("History citation identity is invalid.");
        }
    }
}
