using System.Text.Json.Serialization;

using Kora.Core.Dependencies;
using Kora.Core.Hosting;

namespace Kora.Core.Conversation;

/// <summary>User body is immutable. The separate host status is not an assistant answer or execution receipt.</summary>
public sealed record ConversationMessage(HostRequest Request, HostRevision Generation, long Sequence,
    long Revision, DateTimeOffset SubmittedAt, string Body, ModelTurnOutcome? Outcome, ModelTurnReason? Reason)
{
    public int Version { get; init; } = 1;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ModelTurnProvenance? Execution { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? ExecutionMetadataRevision { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ConversationResponse? AssistantResponse { get; init; }
    [JsonIgnore]
    public bool HasAssistantResponse => AssistantResponse is not null;
    [JsonIgnore]
    public HostTaskState ExpectedTaskState => Outcome switch
    {
        null => Execution is null ? HostTaskState.IntentRecorded : HostTaskState.DispatchRecorded,
        ModelTurnOutcome.Succeeded => HostTaskState.Succeeded,
        ModelTurnOutcome.Cancelled or ModelTurnOutcome.TimedOut when Execution is not null => HostTaskState.Unknown,
        ModelTurnOutcome.Unavailable => HostTaskState.Unavailable,
        ModelTurnOutcome.Cancelled => HostTaskState.Cancelled,
        ModelTurnOutcome.Unknown when Reason == ModelTurnReason.HostInterrupted => HostTaskState.Interrupted,
        ModelTurnOutcome.Unknown => HostTaskState.Unknown,
        _ => HostTaskState.Denied,
    };
    public void Validate()
    {
        if (Version is not (1 or 2) || Request is null || !ConversationMessageProvenance.IsValidGeneration(Generation.Value) || Sequence is <= 0 or > ConversationText.MaximumSessionMessages
            || !ConversationMessageProvenance.IsValidRevision(Revision) || SubmittedAt.Offset != TimeSpan.Zero
            || Request.Origin != RequestOrigin.LocalUi || Request.InvocationId is not null
            || (Outcome is null) != (Reason is null)
            || Outcome is { } outcome && !Enum.IsDefined(outcome)
            || (Version == 1 && (Execution is not null || ExecutionMetadataRevision is not null || AssistantResponse is not null || Outcome == ModelTurnOutcome.Succeeded))
            || (Version == 2 && (Execution is null || Execution.Request != Request
                || Execution.Selection is null || !Execution.Selection.IsValid
                || Execution.Selection.Provider != ModelProviderIdentity.Ollama || ExecutionMetadataRevision is null or < 0))
            || (AssistantResponse is not null) != (Outcome == ModelTurnOutcome.Succeeded)
            || Reason is { } reason && !Enum.IsDefined(reason)
            || (Version == 1 ? (Revision == 1) != (Outcome is null) || Revision > 2
                : (Revision == 2) != (Outcome is null) || Revision < 2))
        { throw new InvalidDataException("The conversation source, revision or terminal host status is invalid."); }
        Request.RequestId.Validate();
        Request.SessionId.Validate();
        Request.TaskId.Validate();
        Execution?.Turn.Validate();
        AssistantResponse?.Validate();
        if (AssistantResponse is { } answer && answer.Provenance != Execution)
        { throw new InvalidDataException("Assistant response does not belong to the retained original execution."); }
        ConversationText.Validate(Body, submission: true);
    }
}
