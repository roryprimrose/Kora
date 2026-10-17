using System.Runtime.InteropServices;

using Kora.Core.Hosting;
using Kora.Core.Interaction;

namespace Kora.Core.Storage;

/// <summary>Exact passive receipt predicates; absent values mean Any, never authority.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SessionHistoryMetadataFilter(
    SessionHistoryKind? Kind = null, HostTaskState? TaskState = null,
    HostInteractionOutcome? Decision = null, QuestionStatus? QuestionStatus = null,
    RequestOrigin? AnswerChannel = null)
{
    public void Validate()
    {
        ValidateDefined(Kind);
        ValidateDefined(TaskState);
        ValidateDefined(Decision);
        ValidateDefined(QuestionStatus);
        ValidateDefined(AnswerChannel);
    }

    public bool Matches(SessionHistoryEvent record) =>
        (Kind is null || Kind == record.Kind)
        && (TaskState is null || TaskState == record.TaskState)
        && (Decision is null || Decision == record.Decision)
        && (QuestionStatus is null || QuestionStatus == record.QuestionStatus)
        && (AnswerChannel is null || AnswerChannel == record.AnswerChannel);

    internal string CanonicalValues => string.Join('\n',
        Kind?.ToString() ?? "Any", TaskState?.ToString() ?? "Any", Decision?.ToString() ?? "Any",
        QuestionStatus?.ToString() ?? "Any", AnswerChannel?.ToString() ?? "Any");

    private static void ValidateDefined<T>(T? value) where T : struct, Enum
    {
        if (value is { } exact && !Enum.IsDefined(exact))
        { throw new ArgumentOutOfRangeException(nameof(value), "The history metadata predicate is undefined."); }
    }
}
