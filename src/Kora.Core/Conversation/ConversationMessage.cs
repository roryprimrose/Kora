using System.Text.Json.Serialization;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.Conversation;

/// <summary>User body is immutable. The separate host status is not an assistant answer or execution receipt.</summary>
public sealed record ConversationMessage(HostRequest Request, HostRevision Generation, long Sequence,
    long Revision, DateTimeOffset SubmittedAt, string Body, ModelTurnOutcome? Outcome, ModelTurnReason? Reason)
{
    public int Version { get; init; } = 1;
    [JsonIgnore]
    public HostTaskState ExpectedTaskState => Outcome switch
    {
        null => HostTaskState.IntentRecorded,
        ModelTurnOutcome.Unavailable => HostTaskState.Unavailable,
        ModelTurnOutcome.Cancelled => HostTaskState.Cancelled,
        ModelTurnOutcome.Unknown when Reason == ModelTurnReason.HostInterrupted => HostTaskState.Interrupted,
        ModelTurnOutcome.Unknown => HostTaskState.Unknown,
        _ => HostTaskState.Denied,
    };
    public void Validate()
    {
        if (Version != 1 || Request is null || !ConversationMessageProvenance.IsValidGeneration(Generation.Value) || Sequence is <= 0 or > ConversationText.MaximumSessionMessages
            || !ConversationMessageProvenance.IsValidRevision(Revision) || SubmittedAt.Offset != TimeSpan.Zero
            || Request.Origin is not (RequestOrigin.LocalUi or RequestOrigin.ActivatedVoice) || Request.InvocationId is not null
            || (Outcome is null) != (Reason is null)
            || Outcome is { } outcome && !Enum.IsDefined(outcome)
            || Outcome == ModelTurnOutcome.Succeeded
            || Reason is { } reason && !Enum.IsDefined(reason)
            || (Revision == 1) != (Outcome is null))
        { throw new InvalidDataException("The conversation source, revision or terminal host status is invalid."); }
        Request.RequestId.Validate();
        Request.SessionId.Validate();
        Request.TaskId.Validate();
        ConversationText.Validate(Body, submission: true);
    }
}
