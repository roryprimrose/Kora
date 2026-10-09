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
    QuestionStatus? QuestionStatus, RequestOrigin? AnswerChannel);
